using KSP.UI;
using KSP.UI.Screens;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HideEmptyFilters
{
    /// <summary>
    /// Hides part-catalog filters (top-level "Filter by ..." buttons) and subcategories that
    /// contain no part the player could actually see in the editor's part list.
    ///
    /// A part counts as visible when it passes the same rules the stock part list uses
    /// (EditorPartList.ExcludeFilters + amountAvailable), so research progress, inaccessible
    /// parts and any exclusion filter added by other mods are honoured automatically.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class HideEmptyFilters : MonoBehaviour
    {
        private const string LogPrefix = "[HideEmptyFilters] ";
        private const string UnpurchasedFilterId = "HideEmptyFilters_UnpurchasedParts";

        // Frames to wait after the categorizer exists, so stock and mods (Filter Extensions,
        // Community Category Kit, ...) can finish building their categories.
        private const int SettleFrames = 10;

        private sealed class HiddenSubcategory
        {
            public PartCategorizer.Category Parent;
            public PartCategorizer.Category Subcategory;
        }

        private readonly List<PartCategorizerButton> hiddenButtons = new List<PartCategorizerButton>();
        private readonly HashSet<PartCategorizer.Category> hiddenSet = new HashSet<PartCategorizer.Category>();
        private readonly List<HiddenSubcategory> hiddenSubcategories = new List<HiddenSubcategory>();
        private readonly List<AvailablePart> visibleParts = new List<AvailablePart>();
        private readonly HashSet<string> reportedFilterErrors = new HashSet<string>();

        private EditorPartListFilter<AvailablePart> unpurchasedFilter;
        private bool countUnpurchasedParts;
        private bool hideUnpurchasedParts;

        private bool ready;
        private bool dirty;
        private int structureSignature;
        private int excludeFilterCount;

        #region Lifecycle

        private void Start()
        {
            GameEvents.OnPartPurchased.Add(OnPartPurchased);
            GameEvents.OnGameSettingsApplied.Add(OnGameSettingsApplied);
            GameEvents.onGUIEditorToolbarReady.Add(OnEditorToolbarReady);

            StartCoroutine(Initialize());
        }

        private void OnDestroy()
        {
            GameEvents.OnPartPurchased.Remove(OnPartPurchased);
            GameEvents.OnGameSettingsApplied.Remove(OnGameSettingsApplied);
            GameEvents.onGUIEditorToolbarReady.Remove(OnEditorToolbarReady);

            if (unpurchasedFilter != null && EditorPartList.Instance != null)
                EditorPartList.Instance.ExcludeFilters.RemoveFilter(unpurchasedFilter);
        }

        private IEnumerator Initialize()
        {
            while (!CategorizerIsReady())
                yield return null;

            for (int i = 0; i < SettleFrames; i++)
                yield return null;

            ReadSettings();
            ApplyUnpurchasedPartsFilter();

            ready = true;
            dirty = true;
            Debug.Log(LogPrefix + "Initialized.");
        }

        private static bool CategorizerIsReady()
        {
            var pc = PartCategorizer.Instance;
            return pc != null
                && EditorPartList.Instance != null
                && pc.filters != null
                && pc.filters.Count > 0
                && pc.filters.TrueForAll(f => f != null && f.button != null);
        }

        // LateUpdate runs after the stock UI code of the frame and before the canvas is
        // rendered, so any button the stock code re-activated (it does so every time it
        // re-inserts subcategory buttons into its list) is hidden again without flicker.
        private void LateUpdate()
        {
            if (!ready) return;

            var pc = PartCategorizer.Instance;
            var partList = EditorPartList.Instance;
            if (pc == null || partList == null) return;

            int signature = ComputeStructureSignature(pc);
            int excludeCount = partList.ExcludeFilters.Count;

            if (dirty || signature != structureSignature || excludeCount != excludeFilterCount)
            {
                structureSignature = signature;
                excludeFilterCount = excludeCount;
                dirty = false;
                Rebuild(pc);
            }

            EnforceHidden(pc);
        }

        #endregion

        #region Events

        private void OnPartPurchased(AvailablePart part) => dirty = true;

        private void OnEditorToolbarReady() => dirty = true;

        private void OnGameSettingsApplied()
        {
            if (!ready) return;

            ReadSettings();
            ApplyUnpurchasedPartsFilter();
            dirty = true;
        }

        #endregion

        #region Settings / unpurchased parts

        private void ReadSettings()
        {
            var settings = HighLogic.CurrentGame?.Parameters?.CustomParams<HideEmptyFiltersParams>();
            bool isCareer = HighLogic.CurrentGame != null && HighLogic.CurrentGame.Mode == Game.Modes.CAREER;

            // Purchasing only exists in Career mode.
            countUnpurchasedParts = isCareer && settings != null && settings.countUnpurchasedPartsAsUnavailable;
            hideUnpurchasedParts = isCareer && settings != null && settings.hideUnpurchasedPartsInVAB;
        }

        // Instead of hiding part icons by hand, register a stock exclusion filter: the part
        // list, the search results and other mods reading the list (e.g. VAB Organizer)
        // then all leave unpurchased parts out on their own.
        private void ApplyUnpurchasedPartsFilter()
        {
            var partList = EditorPartList.Instance;
            if (partList == null) return;

            if (unpurchasedFilter == null)
                unpurchasedFilter = new EditorPartListFilter<AvailablePart>(UnpurchasedFilterId, IsPurchased);

            bool present = partList.ExcludeFilters[UnpurchasedFilterId] != null;
            if (hideUnpurchasedParts == present) return;

            if (hideUnpurchasedParts)
                partList.ExcludeFilters.AddFilter(unpurchasedFilter);
            else
                partList.ExcludeFilters.RemoveFilter(unpurchasedFilter);

            partList.Refresh();
        }

        private static bool IsPurchased(AvailablePart part) =>
            part != null && ResearchAndDevelopment.PartModelPurchased(part);

        #endregion

        #region Building the hidden list

        private static int ComputeStructureSignature(PartCategorizer pc)
        {
            unchecked
            {
                int hash = pc.filters.Count;
                for (int i = 0; i < pc.filters.Count; i++)
                {
                    var filter = pc.filters[i];
                    hash = hash * 31 + (filter?.subcategories?.Count ?? -1);
                }
                return hash;
            }
        }

        private void Rebuild(PartCategorizer pc)
        {
            var previouslyHidden = new List<PartCategorizerButton>(hiddenButtons);

            hiddenButtons.Clear();
            hiddenSet.Clear();
            hiddenSubcategories.Clear();

            CollectVisibleParts();

            int hiddenFilters = 0;
            int hiddenSubs = 0;

            foreach (var filter in pc.filters)
            {
                if (filter == null || filter.button == null || filter.subcategories == null) continue;

                bool filterHasContent = false;

                foreach (var sub in filter.subcategories)
                {
                    if (sub == null || sub.button == null) continue;

                    if (!CanEvaluate(sub) || HasVisiblePart(sub))
                    {
                        filterHasContent = true;
                        continue;
                    }

                    hiddenButtons.Add(sub.button);
                    hiddenSet.Add(sub);
                    hiddenSubcategories.Add(new HiddenSubcategory { Parent = filter, Subcategory = sub });
                    hiddenSubs++;
                }

                // A filter without subcategories is left alone: there is nothing to judge it by.
                if (!filterHasContent && filter.subcategories.Count > 0)
                {
                    hiddenButtons.Add(filter.button);
                    hiddenSet.Add(filter);
                    hiddenFilters++;
                }
            }

            // Restore buttons that are no longer empty (e.g. after purchasing a part).
            foreach (var button in previouslyHidden)
            {
                if (button != null && !hiddenButtons.Contains(button))
                    Restore(pc, button);
            }

            Debug.Log(LogPrefix + $"Checked {pc.filters.Count} filters against {visibleParts.Count} visible parts: " +
                      $"hiding {hiddenFilters} filters and {hiddenSubs} subcategories.");
        }

        private void CollectVisibleParts()
        {
            visibleParts.Clear();

            List<AvailablePart> candidates;
            try
            {
                candidates = EditorPartList.Instance.ExcludeFilters.GetFilteredList(PartLoader.LoadedPartsList);
            }
            catch (Exception ex)
            {
                // A broken exclusion filter from another mod must not make us hide everything.
                Debug.LogError(LogPrefix + "An editor exclusion filter threw; falling back to research state only.");
                Debug.LogException(ex);
                candidates = new List<AvailablePart>(PartLoader.LoadedPartsList);
                candidates.RemoveAll(p => p == null || !ResearchAndDevelopment.PartTechAvailable(p));
            }

            foreach (var part in candidates)
            {
                if (part == null || part.amountAvailable <= 0) continue;
                if (countUnpurchasedParts && !IsPurchased(part)) continue;
                visibleParts.Add(part);
            }
        }

        // Only subcategories that list parts can be judged. Subassembly / variant lists are
        // never hidden.
        private static bool CanEvaluate(PartCategorizer.Category sub) =>
            sub.exclusionFilter != null &&
            (sub.displayType == EditorPartList.State.PartsList ||
             sub.displayType == EditorPartList.State.CustomPartList);

        private bool HasVisiblePart(PartCategorizer.Category sub)
        {
            var criteria = sub.exclusionFilter.FilterCriteria;
            if (criteria == null) return true;

            try
            {
                for (int i = 0; i < visibleParts.Count; i++)
                {
                    if (criteria(visibleParts[i]))
                        return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                // Keep the subcategory visible if its filter misbehaves; log once per filter.
                string id = sub.exclusionFilter.ID ?? sub.button.categoryName;
                if (reportedFilterErrors.Add(id))
                {
                    Debug.LogError(LogPrefix + $"Filter of subcategory \"{sub.button.categoryName}\" threw; leaving it visible.");
                    Debug.LogException(ex);
                }
                return true;
            }
        }

        #endregion

        #region Applying

        private static GameObject GetButtonObject(PartCategorizerButton button) =>
            button.container != null ? button.container.gameObject : button.gameObject;

        private void EnforceHidden(PartCategorizer pc)
        {
            for (int i = 0; i < hiddenButtons.Count; i++)
            {
                var button = hiddenButtons[i];
                if (button == null) continue;

                var go = GetButtonObject(button);
                if (go.activeSelf)
                    go.SetActive(false);
            }

            if (pc.IsTransitioning) return;

            // When a filter is opened and none of its subcategories is selected, the stock
            // code selects the first one. If that one is hidden, the player would be looking
            // at an empty part list, so move the selection to the first visible subcategory.
            for (int i = 0; i < hiddenSubcategories.Count; i++)
            {
                var entry = hiddenSubcategories[i];
                var radio = entry.Subcategory.button != null ? entry.Subcategory.button.activeButton : null;
                if (radio == null || !radio.Value) continue;

                var replacement = FindFirstVisibleSibling(entry.Parent);
                if (replacement == null) continue;

                var replacementRadio = replacement.button.activeButton;
                if (!replacementRadio.Value)
                    replacementRadio.SetState(UIRadioButton.State.True, UIRadioButton.CallType.APPLICATION, null, popButtonsInGroup: false);

                // Select the replacement first: deselecting the last selected subcategory
                // would otherwise deselect its parent filter as well.
                radio.SetState(UIRadioButton.State.False, UIRadioButton.CallType.APPLICATION, null, popButtonsInGroup: false);
            }
        }

        private PartCategorizer.Category FindFirstVisibleSibling(PartCategorizer.Category parent)
        {
            if (parent?.subcategories == null) return null;

            foreach (var sub in parent.subcategories)
            {
                if (sub == null || sub.button == null || sub.button.activeButton == null) continue;
                if (!hiddenSet.Contains(sub))
                    return sub;
            }
            return null;
        }

        private static void Restore(PartCategorizer pc, PartCategorizerButton button)
        {
            var container = button.container;
            if (container == null)
            {
                button.gameObject.SetActive(true);
                return;
            }

            // Only re-activate buttons that the stock UI currently has in one of its lists;
            // buttons parked in the (inactive) repository are re-activated by the stock code
            // itself when it inserts them again.
            bool listed = (pc.scrollListMain != null && pc.scrollListMain.GetIndex(container) >= 0)
                       || (pc.scrollListSub != null && pc.scrollListSub.GetIndex(container) >= 0);

            if (listed)
                container.gameObject.SetActive(true);
        }

        #endregion
    }
}
