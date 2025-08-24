using System.Collections;
using System.Reflection;

namespace HideEmptyFilters
{
    class HideEmptyFiltersParams : GameParameters.CustomParameterNode
    {
        public override string Title { get { return ""; } } // column heading
        public override GameParameters.GameMode GameMode { get { return GameParameters.GameMode.CAREER; } }
        public override string Section { get { return "HideEmptyFilters"; } }
        public override string DisplaySection { get { return "Hide Empty Filters"; } }
        public override int SectionOrder { get { return 1; } }
        public override bool HasPresets { get { return false; } }

        [GameParameters.CustomParameterUI(
        "Treat Unpurchased Parts as Unavailable",
        toolTip = "When this setting is enabled, any filter that only contains unpurchased parts will be hidden.\n\n" +
                    "In Career mode, parts that you have unlocked in the tech tree but have not yet purchased with funds will be treated as locked.",
        autoPersistance = true)]
        public bool countUnpurchasedPartsAsUnavailable = false;

        [GameParameters.CustomParameterUI(
        "Hide Unpurchased Parts in VAB",
        toolTip = "When this setting is enabled, unpurchased parts in Career mode will be hidden from the parts list in the VAB and SPH.",
        autoPersistance = true)]
        public bool hideUnpurchasedPartsInVAB = false;

        public override bool Enabled(MemberInfo member, GameParameters parameters)
        {
            return true;
        }

        public override bool Interactible(MemberInfo member, GameParameters parameters)
        {
            return true;
        }

        public override IList ValidValues(MemberInfo member)
        {
            return null;
        }
    }
}
