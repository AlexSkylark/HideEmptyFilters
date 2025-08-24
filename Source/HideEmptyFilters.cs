using KSP.UI;
using KSP.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;
using UnityEngine.UI;

namespace HideEmptyFilters
{
    public class BlockClickWhenActive : MonoBehaviour, IPointerDownHandler, IPointerClickHandler, ICanvasRaycastFilter
    {
        public bool IsActive;

        // 1) este método evita que o GraphicRaycaster sequer considere este alvo quando ativo
        public bool IsRaycastLocationValid(Vector2 sp, Camera eventCamera) => !IsActive;

        public void OnPointerDown(PointerEventData e)
        {
            if (!IsActive) return;
            Kill(e);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (!IsActive) return;
            // redundância, caso algum módulo ainda dispare click
            Kill(e);
        }

        private static void Kill(PointerEventData e)
        {
            e.Use();
            e.eligibleForClick = false;
            e.clickCount = 0;
            e.pointerPress = null;
            e.rawPointerPress = null;
            // opcional: limpa seleção pra não disparar Select/Submit
            if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == e.pointerPress)
                EventSystem.current.SetSelectedGameObject(null);
        }
    }

    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class HideEmptyFilters : MonoBehaviour
    {
        private List<PartCategorizerButton> buttonsToHide = new List<PartCategorizerButton>();
        bool countUnpurchasedParts;
        bool hideUnpurchasedParts;
        List<AvailablePart> unpurchasedParts = new List<AvailablePart>();
        bool isHidingParts = false;
        private readonly List<BlockClickWhenActive> _categoryBlockers = new List<BlockClickWhenActive>();
        private bool _wiredOnce;
        private readonly HashSet<UnityEngine.Object> _wired = new HashSet<UnityEngine.Object>();


        private Coroutine partCategorizerCoroutine;

        private void Awake()
        {
            // Ensure this GameObject persists across scenes
            DontDestroyOnLoad(gameObject);

            // Register event handlers
            GameEvents.onLevelWasLoadedGUIReady.Add(OnSceneLoad);
            GameEvents.onGameSceneLoadRequested.Add(OnSceneChange);
        }

        private void OnDestroy()
        {
            // Cleanup when the object is destroyed
            GameEvents.onLevelWasLoadedGUIReady.Remove(OnSceneLoad);
            GameEvents.onGameSceneLoadRequested.Remove(OnSceneChange);
        }

        void OnEnable()
        {
            // Dispara quando a GUI do editor sobe
            GameEvents.onGUIEditorToolbarReady.Add(OnEditorGUIReady);

            // Dispara quando entra/reinicia o editor (VAB/SPH)
            GameEvents.onEditorStarted.Add(OnEditorStarted);
        }

        void OnDisable()
        {
            GameEvents.onGUIEditorToolbarReady.Remove(OnEditorGUIReady);
            GameEvents.onEditorStarted.Remove(OnEditorStarted);
        }

        private void OnSceneLoad(GameScenes scene)
        {
            // Run initialization only in the VAB/SPH
            if (HighLogic.LoadedSceneIsEditor)
            {
                Debug.Log("[HideEmptyFilters] Entering the editor. Initializing...");
                StartToolbarCoroutine();
            }
        }

        private void OnSceneChange(GameScenes scene)
        {
            // Stop coroutines and clean up before leaving the editor
            if (scene != GameScenes.EDITOR)
            {
                Debug.Log("[HideEmptyFilters] Leaving the editor. Cleaning up...");
                StopAllCoroutines();
                partCategorizerCoroutine = null;
            }
        }       

        private void OnEditorGUIReady()
        {
            // Dá um frame pra UI assentar e tenta armar os ganchos
            StartCoroutine(InitWhenCategorizerReady());
        }

        private void OnEditorStarted()
        {
            StartCoroutine(InitWhenCategorizerReady());
        }

        private IEnumerator InitWhenCategorizerReady()
        {
            yield return new WaitUntil(() =>
                PartCategorizer.Instance != null &&
                PartCategorizer.Instance.filters != null &&
                PartCategorizer.Instance.filters.Count > 0 &&
                PartCategorizer.Instance.filters.TrueForAll(f => f != null && f.button != null)
            );

            yield return null;

            RegisterCategoriesWithBlocking();
            RegisterSubcategoryClickEvent();
            MarkInitiallyActiveCategory();

            _wiredOnce = true;
        }

        private void MarkInitiallyActiveCategory()
        {
            if (_categoryBlockers == null || _categoryBlockers.Count == 0) return;

            for (int i = 0; i < _categoryBlockers.Count; i++)
            {
                var blocker = _categoryBlockers[i];
                if (blocker == null) continue;

                blocker.IsActive = (i == 0); // first tab is active when scene loads
            }
        }

        private void RemoveEventTriggersUnder(GameObject go)
        {
            if (!go) return;
            foreach (var trig in go.GetComponentsInChildren<EventTrigger>(true))
                Destroy(trig);
        }

        private bool TryGetUiFor(PartCategorizerButton pcb, out UIRadioButton radio, out Button stdBtn)
        {
            radio = null;
            stdBtn = null;
            if (!pcb) return false;

            radio = pcb.GetComponent<UIRadioButton>() ?? pcb.GetComponentInChildren<UIRadioButton>(true);
            if (radio) return true;

            stdBtn = pcb.GetComponent<Button>() ?? pcb.GetComponentInChildren<Button>(true);
            return stdBtn != null;
        }

        private void RegisterAdvancedModeToggleClickEvent()
        {
            HookTopBarButton("_UIMaster/MainCanvas/Editor/Top Bar/Button Arrow Left");
            HookTopBarButton("_UIMaster/MainCanvas/Editor/Top Bar/Button Arrow Right");

            foreach (var cat in PartCategorizer.Instance.filters)
                WireCategoryButton(cat.button);
        }

        private void RegisterSubcategoryClickEvent()
        {
            foreach (var cat in PartCategorizer.Instance.filters)
                foreach (var sub in cat.subcategories)
                    WireSubcategoryButton(sub.button);
        }

        private void HookTopBarButton(string path)
        {
            var go = GameObject.Find(path);
            if (!go) return;

            RemoveEventTriggersUnder(go);

            var btn = go.GetComponent<Button>() ?? go.GetComponentInChildren<Button>(true);
            if (btn && !_wired.Contains(btn))
            {
                btn.onClick.AddListener(OnToolbarRelatedClick);
                _wired.Add(btn);
            }
        }

        private void WireCategoryButton(PartCategorizerButton pcb)
        {
            if (!TryGetUiFor(pcb, out var radio, out var stdBtn)) return;

            RemoveEventTriggersUnder(pcb.gameObject);

            if (radio && !_wired.Contains(radio))
            {
                radio.onClick.AddListener((PointerEventData _e, UIRadioButton.State _s, UIRadioButton.CallType _c) =>
                    OnToolbarRelatedClick());

                radio.onTrue.AddListener((PointerEventData _e, UIRadioButton.CallType _c) => OnToolbarRelatedClick());
                _wired.Add(radio);
                return;
            }

            if (stdBtn && !_wired.Contains(stdBtn))
            {
                stdBtn.onClick.AddListener(OnToolbarRelatedClick);
                _wired.Add(stdBtn);
            }
        }

        private void WireSubcategoryButton(PartCategorizerButton pcb)
        {
            if (!TryGetUiFor(pcb, out var radio, out var stdBtn)) return;

            RemoveEventTriggersUnder(pcb.gameObject);

            if (radio && !_wired.Contains(radio))
            {
                radio.onClick.AddListener((PointerEventData _e, UIRadioButton.State _s, UIRadioButton.CallType _c) =>
                    OnSubcategoryClicked());
                radio.onTrue.AddListener((PointerEventData _e, UIRadioButton.CallType _c) => OnSubcategoryClicked());
                _wired.Add(radio);
                return;
            }

            if (stdBtn && !_wired.Contains(stdBtn))
            {
                stdBtn.onClick.AddListener(OnSubcategoryClicked);
                _wired.Add(stdBtn);
            }
        }

        private void OnToolbarRelatedClick()
        {
            UpdateSubcategoryStates();

            if (hideUnpurchasedParts)
            {
                isHidingParts = true;
                var partsRoot = GameObject
                    .Find("_UIMaster/MainCanvas/Editor/Panel Parts List/Mode Transition/PartList Area/PartList and sorting/ListAndScrollbar/ScrollRect")
                    .GetComponent<ScrollRect>();

                TogglePartsRoot(partsRoot, false);
                StartCoroutine(UpdateHiddenUnpurchasedParts(partsRoot));
            }
        }

        private void OnSubcategoryClicked()
        {
            if (isHidingParts) return;
            isHidingParts = true;

            var partsRoot = GameObject
                .Find("_UIMaster/MainCanvas/Editor/Panel Parts List/Mode Transition/PartList Area/PartList and sorting/ListAndScrollbar/ScrollRect")
                .GetComponent<ScrollRect>();

            TogglePartsRoot(partsRoot, false);
            StartCoroutine(UpdateHiddenUnpurchasedParts(partsRoot));
        }

        private void RegisterCategoriesWithBlocking()
        {
            _categoryBlockers.Clear();

            foreach (var cat in PartCategorizer.Instance.filters)
            {
                var pcb = cat.button;
                if (!pcb) continue;

                var radio = pcb.GetComponent<UIRadioButton>() ?? pcb.GetComponentInChildren<UIRadioButton>(true);
                if (!radio) continue;

                foreach (var trig in pcb.GetComponentsInChildren<EventTrigger>(true))
                    Destroy(trig);

                var blocker = radio.gameObject.GetComponent<BlockClickWhenActive>() ??
                              radio.gameObject.AddComponent<BlockClickWhenActive>();
                blocker.IsActive = false;

                _categoryBlockers.Add(blocker);

                radio.onTrue.AddListener((PointerEventData _p, UIRadioButton.CallType _c) =>
                {
                    for (int i = 0; i < _categoryBlockers.Count; i++)
                        _categoryBlockers[i].IsActive = false;

                    blocker.IsActive = true;
                });

                radio.onFalse.AddListener((PointerEventData _p, UIRadioButton.CallType _c) =>
                {
                    blocker.IsActive = false;
                });

                radio.onClick.AddListener((PointerEventData _e, UIRadioButton.State _s, UIRadioButton.CallType _c) =>
                    OnToolbarRelatedClick());

                radio.onTrue.AddListener((PointerEventData _p, UIRadioButton.CallType _c) => OnToolbarRelatedClick());
            }
        }

        private void StartToolbarCoroutine()
        {
            if (partCategorizerCoroutine == null)
            {
                partCategorizerCoroutine = StartCoroutine(WaitForPartCategorizer());
            }
        }

        private IEnumerator WaitForPartCategorizer()
        {
            // Wait for PartCategorizer to be ready
            while (PartCategorizer.Instance == null)
            {
                yield return null;
            }

            // wait 10 extra frames to be safe
            for (int i = 0; i < 10; i++) // Adjust the number of frames if necessary
            {
                yield return null;
            }

            countUnpurchasedParts = HighLogic.CurrentGame.Parameters.CustomParams<HideEmptyFiltersParams>().countUnpurchasedPartsAsUnavailable;
            hideUnpurchasedParts = HighLogic.CurrentGame.Parameters.CustomParams<HideEmptyFiltersParams>().hideUnpurchasedPartsInVAB;

            BuildFiltersToHide();
            
            UpdateSubcategoryStates();
            RegisterAdvancedModeToggleClickEvent();

            if (hideUnpurchasedParts)
            {
                BuildPartsToHide();
                RegisterSubcategoryClickEvent();
            }
        }

        private IEnumerator UpdateHiddenUnpurchasedParts(ScrollRect partsRoot) =>
            UpdateHiddenUnpurchasedParts(partsRoot, true);

        private IEnumerator UpdateHiddenUnpurchasedParts(ScrollRect partsRoot, bool temporarilyHideRoot)
        {
            if (temporarilyHideRoot) TogglePartsRoot(partsRoot, false);

            yield return null;

            var partsListBase = partsRoot.gameObject.GetChild("PartGrid_Base");
            var componentPartsList = partsListBase.GetComponentsInChildren<EditorPartIcon>().ToList();

            foreach (var part in componentPartsList)
            {
                if (unpurchasedParts.Any(p => p.name == part.partInfo.name))
                {
                    part.gameObject.SetActive(false);
                }
            }

            // VAB Organizer is installed. Process headers.
            foreach (var groupObj in componentPartsList.Select(p => p.transform.parent).Distinct()) {
                bool hasActiveParts = false;
                foreach(var partObject in groupObj.GetComponentsInChildren<EditorPartIcon>().Select(pi => pi.gameObject))
                {
                    if (partObject.activeSelf)
                    {
                        hasActiveParts = true;
                        break;
                    }
                }

                if (!hasActiveParts)
                {
                    var headerObject = groupObj.transform.parent.GetChild(groupObj.transform.GetSiblingIndex() - 1);
                    headerObject.gameObject.SetActive(false);

                    var firstHeader = groupObj.transform.parent.GetComponentInChildren<TextMeshProUGUI>();
                    if (firstHeader != null)
                    {
                        yield return null;
                        ExecuteEvents.Execute(
                            target: firstHeader.transform.parent.gameObject,
                            eventData: new BaseEventData(EventSystem.current),
                            functor: ExecuteEvents.submitHandler
                        );

                        yield return null;
                        ExecuteEvents.Execute(
                            target: firstHeader.transform.parent.gameObject,
                            eventData: new BaseEventData(EventSystem.current),
                            functor: ExecuteEvents.submitHandler
                        );
                    }
                }
            }

            TogglePartsRoot(partsRoot, true);

            if (temporarilyHideRoot) TogglePartsRoot(partsRoot, true);

            isHidingParts = false;
        }

        private void UpdateSubcategoryStates()
        {
            int buttonsHidden = 0;
            Debug.Log($"[HideEmptyFilters] Processing list of filters to hide...");
            foreach (var button in buttonsToHide)
            {
                // Check if the state changed from false to true
                if (button.gameObject.activeSelf)
                {
                    button.gameObject.SetActive(false);
                    buttonsHidden++;
                    Debug.Log($"[HideEmptyFilters] Executed hide command on (sub)category \"{button.displayCategoryName}\".");
                }
            }
            Debug.Log($"[HideEmptyFilters] buttons list processed. {buttonsHidden} filters were hidden.");           
        }

        private void BuildFiltersToHide()
        {
            Profiler.BeginSample("HideEmptyFilters-BuildList");

            buttonsToHide.Clear();

            foreach (var category in PartCategorizer.Instance.filters)
            {
                Debug.Log($"[HideEmptyFilters] Processing category: {category.button.displayCategoryName}");
                try
                {
                    bool categoryHasParts = false;

                    foreach (var subcategory in category.subcategories.Where(s => s != null))
                    {
                        Debug.Log($"[HideEmptyFilters] Processing subcategory: {category.button.displayCategoryName}");
                        if (SubcategoryHasParts(subcategory))
                        {
                            // At least one subcategory has parts
                            categoryHasParts = true;
                            continue;
                        }
                        else
                        {
                            buttonsToHide.Add(subcategory.button);
                            Debug.Log($"[HideEmptyFilters] Added subcategory \"{subcategory.button.displayCategoryName}\" from category \"{category.button.displayCategoryName}\" to the hiding list.");
                        }
                    }

                    // No subcategories have parts
                    if (!categoryHasParts)
                    {
                        buttonsToHide.Add(category.button);
                        Debug.Log($"[HideEmptyFilters] Added category \"{category.button.displayCategoryName}\" to the hiding list.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
                finally
                {
                    Profiler.EndSample();
                }
            }
        }

        private void BuildPartsToHide()
        {
            foreach (var part in PartLoader.LoadedPartsList)
            {
                if (HighLogic.CurrentGame.Mode == Game.Modes.CAREER) // Only relevant in Career mode
                {
                    bool isPurchased =
                        ResearchAndDevelopment.GetTechnologyState(part.TechRequired) == RDTech.State.Available && 
                        ResearchAndDevelopment.PartModelPurchased(part);
                    if (!isPurchased)
                    {
                        // Hide the part if it is not purchased
                        Debug.Log($"[HideEmptyFilters] Adding unpurchased part to list: {part.title}");
                        unpurchasedParts.Add(part);
                    }
                }
            }
        }
        
        bool SubcategoryHasParts(PartCategorizer.Category subcategory)
        {
            foreach (var part in PartLoader.LoadedPartsList)
            {
                AvailablePart currentPart = part;
                try
                {
                    if (IsPartAvailable(part)) {
                        if (subcategory.exclusionFilter.FilterCriteria(part))
                        {
                            // At least one part matches the subcategory
                            Debug.Log($"[HideEmptyFilters] Found part \"{part.name}\" in subcategory \"{subcategory.button.displayCategoryName}\"!");
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.Log($"[HideEmptyFilters] ERROR! While processing part {currentPart.name} of subcategory \"{subcategory.button.displayCategoryName}\".");
                    throw ex;
                }
                
            }
            // No parts matched the subcategory
            return false;
        }

        bool IsPartAvailable(AvailablePart part)
        {
            if (ResearchAndDevelopment.GetTechnologyState(part.TechRequired) != RDTech.State.Available)
            {
                return false; // Part is not unlocked in the tech tree
            }

            if (countUnpurchasedParts && !ResearchAndDevelopment.PartModelPurchased(part))
            {
                return false; // Part is unlocked but not purchased
            }

            return true; // Part is available
        }

        private void TogglePartsRoot(ScrollRect partsRoot, bool activate)
        {
            if (partsRoot == null) return;

            var cg = partsRoot.GetComponent<CanvasGroup>();
            if (cg == null) cg = partsRoot.gameObject.AddComponent<CanvasGroup>();

            if (activate)
            {
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = true;

                partsRoot.enabled = true;

                Canvas.ForceUpdateCanvases();
                var content = partsRoot.content;
                if (content != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }
            else
            {
                cg.alpha = 0f;
                cg.blocksRaycasts = false;
                cg.interactable = false;

                partsRoot.enabled = false;
            }
        }
    }
}