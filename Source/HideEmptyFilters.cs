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
        int hiddenCategoriesCount = 0;
        bool sceneLoadEventExecuted = false;
        AvailablePart currentPart;
        private Dictionary<PartCategorizer.Category, bool> previousSubcategoryStates = new Dictionary<PartCategorizer.Category, bool>();

        private void Start()
        {
            if (HighLogic.CurrentGame.Mode != Game.Modes.SANDBOX)
                GameEvents.onLevelWasLoadedGUIReady.Add(OnSceneLoad);
        }

        private void OnSceneLoad(GameScenes scene)
        {
            if (!sceneLoadEventExecuted)
            {
                Debug.Log($"[HideEmptyFilters] Scene loaded: { scene }");
                if (scene == GameScenes.EDITOR)
                {
                    RegisterCategoryButtonListeners();
                    UpdateAllCategories();
                    StartCoroutine(PollSubcategoryStates());
                }
            }
            sceneLoadEventExecuted = true;
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            sceneLoadEventExecuted = false;
        }

        private IEnumerator PollSubcategoryStates()
        {
            while (true)
            {
                MonitorSubcategoryStates();
                yield return new WaitForSeconds(0.1f); // Adjust polling interval as needed
            }
        }

        private void MonitorSubcategoryStates()
        {
            // Find the "Filter by Function" category
            var filterByFunctionCategory = PartCategorizer.Instance.filters.FirstOrDefault(c => c?.button?.categoryName == "Filter by Function");

            if (filterByFunctionCategory == null || filterByFunctionCategory.subcategories == null)
                return;

            // Ensure the category has a state tracker for its subcategories
            foreach (var subcategory in filterByFunctionCategory.subcategories)
            {
                if (subcategory?.button?.gameObject == null)
                    continue;

                bool isActive = subcategory.button.gameObject.activeSelf;

                // Check if the state changed from false to true
                if (previousSubcategoryStates.TryGetValue(subcategory, out bool wasActive) && !wasActive && isActive)
                {
                    Debug.Log($"[HideEmptyFilters] Subcategory '{subcategory.button.categoryName}' in 'Filter by Function' changed from inactive to active.");
                    UpdateCategory(filterByFunctionCategory); // Trigger your update logic

                    // No need to check further; a change was detected
                    return;
                }

                // Update the tracked state
                previousSubcategoryStates[subcategory] = isActive;
            }
        }

        private void RegisterCategoryButtonListeners()
        {
            // Loop through all categories in PartCategorizer
            foreach (var category in PartCategorizer.Instance.filters)
            {
                EventTrigger categoryTrigger = category.button.btnToggleGeneric.gameObject.AddComponent<EventTrigger>();
                EventTrigger.Entry categoryEntry = new EventTrigger.Entry
                {
                    eventID = EventTriggerType.PointerClick
                };

                categoryEntry.callback.AddListener((eventData) =>
                {
                    UpdateCategory(category);
                });

                categoryTrigger.triggers.Add(categoryEntry);
            }
        }

        private void UpdateAllCategories()
        {

            foreach (var category in PartCategorizer.Instance.filters)
            {
                UpdateCategory(category);
            }
        }

        private void UpdateCategory(PartCategorizer.Category category)
        {
            Profiler.BeginSample("HideEmptyFilters-UpdateCategory");
            hiddenCategoriesCount = 0;

            try
            {
                bool categoryHasParts = false;

                foreach (var subcategory in category.subcategories)
                {
                    bool subcategoryHasParts = false;
                    foreach (var part in PartLoader.LoadedPartsList)
                    {
                        currentPart = part;

                        if (part.TechRequired != null && part.TechRequired.ToUpper() != "UNRESEARCHEABLE")
                        {
                            try
                            {
                                if (ResearchAndDevelopment.GetTechnologyState(part.TechRequired) == RDTech.State.Available)
                                {
                                    if (subcategory.exclusionFilter.FilterCriteria(part))
                                    {
                                        categoryHasParts = true;
                                        subcategoryHasParts = true;
                                        break;
                                    }
                                }
                            }
                            catch (NullReferenceException)
                            {
                                continue;
                            }
                        }
                    }

                    if (!subcategoryHasParts)
                    {
                        subcategory.button.gameObject.SetActive(false);
                        hiddenCategoriesCount++;
                    }
                }

                if (!categoryHasParts)
                {
                    category.button.gameObject.SetActive(false);
                    hiddenCategoriesCount++;
                }

                Debug.Log($"[HideEmptyFilters] Category \"{category.button.categoryName }\" processed. Cleaned { hiddenCategoriesCount } empty filters.");

            }
            catch (Exception ex)
            {
                Debug.Log($"[HideEmptyFilters] Crashed while analyzing part \"{ currentPart.name }");
                Debug.LogException(ex);
            }
            finally
            {
                Profiler.EndSample();
            }

        }
    }
}