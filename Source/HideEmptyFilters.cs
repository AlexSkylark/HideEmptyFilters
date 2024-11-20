using KSP.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Profiling;

namespace HideEmptyFilters
{
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class HideEmptyFilters : MonoBehaviour
    {
        private List<PartCategorizerButton> buttonsToHide = new List<PartCategorizerButton>();
        bool countUnpurchasedParts;

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

            countUnpurchasedParts = HighLogic.CurrentGame.Parameters.CustomParams<Parameters>().countUnpurchasedPartsAsUnavailable;

            BuildFiltersToHide();
            UpdateSubcategoryStates();
            RegisterAdvancedModeToggleClickEvent();
        }

        private void RegisterAdvancedModeToggleClickEvent()
        {
            var smpModeToggleButton = GameObject.Find("_UIMaster/MainCanvas/Editor/Top Bar/Button Arrow Left");
            var advModeToggleButton = GameObject.Find("_UIMaster/MainCanvas/Editor/Top Bar/Button Arrow Right");

            EventTrigger smpModeTrigger = smpModeToggleButton.AddComponent<EventTrigger>();
            EventTrigger advModeTrigger = advModeToggleButton.AddComponent<EventTrigger>();

            EventTrigger.Entry updateEvent = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick
            };

            updateEvent.callback.AddListener((eventData) =>
            {
                UpdateSubcategoryStates();
            });

            smpModeTrigger.triggers.Add(updateEvent);
            advModeTrigger.triggers.Add(updateEvent);
            foreach (var category in PartCategorizer.Instance.filters)
            {
                EventTrigger categoryTrigger = category.button.btnToggleGeneric.gameObject.AddComponent<EventTrigger>();
                categoryTrigger.triggers.Add(updateEvent);
            }
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
    }
}