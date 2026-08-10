using System.Collections.Generic;

namespace MachineDataAcquisitionSystem.Core.Mapping
{
    public static class MappingSavePolicy
    {
        public static bool RequiresSample(
            ParseRuleVersion currentVersion,
            bool hasContentChanges)
        {
            if (currentVersion == null || hasContentChanges)
                return true;

            return currentVersion.Status != ParseRuleStatus.Validated &&
                   currentVersion.Status != ParseRuleStatus.Published;
        }

        public static ParseRuleVersion SelectPreferredEditorVersion(
            IEnumerable<ParseRuleVersion> versions)
        {
            ParseRuleVersion latest = null;
            ParseRuleVersion latestReusable = null;
            if (versions == null) return null;

            foreach (ParseRuleVersion version in versions)
            {
                if (version == null) continue;
                if (latest == null || version.VersionNumber > latest.VersionNumber)
                    latest = version;

                if ((version.Status == ParseRuleStatus.Validated ||
                     version.Status == ParseRuleStatus.Published) &&
                    (latestReusable == null || version.VersionNumber > latestReusable.VersionNumber))
                {
                    latestReusable = version;
                }
            }

            return latestReusable ?? latest;
        }
    }
}
