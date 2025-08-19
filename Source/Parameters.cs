using UnityEngine;

namespace HideEmptyFilters
{
    class Parameters : GameParameters.CustomParameterNode
    {
        [GameParameters.CustomParameterUI(
        "Treat Unpurchased Parts as Unavailable",
        toolTip =   "When this setting is enabled, any filter that only contains unpurchased parts will be hidden.\n\n" +
                    "In Career mode, parts that you have unlocked in the tech tree but have not yet purchased with funds will be treated as locked.",

        autoPersistance = true)]
        public bool countUnpurchasedPartsAsUnavailable = false;

        [GameParameters.CustomParameterUI(
        "Hide Unpurchased Parts in VAB",
        toolTip = "When this setting is enabled, unpurchased parts in Career mode will be hidden from the parts list in the VAB and SPH.",

        autoPersistance = true)]
        public bool hideUnpurchasedPartsInVAB = false;

        public override string Title => "Hide Empty Filters"; // Section Title
        public override GameParameters.GameMode GameMode => GameParameters.GameMode.CAREER;
        public override string Section => "Hide Empty Filters"; // Main Category
        public override string DisplaySection => "Hide Empty Filters"; // Displayed Section Name
        public override int SectionOrder => 1; // Determines the order of your section
        public override bool HasPresets => false; // If you have predefined presets, set this to true
    }
}
