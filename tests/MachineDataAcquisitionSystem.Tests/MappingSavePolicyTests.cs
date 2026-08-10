using MachineDataAcquisitionSystem.Core.Mapping;
using System.Collections.Generic;
using Xunit;

namespace MachineDataAcquisitionSystem.Tests.Mapping
{
    public sealed class MappingSavePolicyTests
    {
        [Theory]
        [InlineData(ParseRuleStatus.Validated)]
        [InlineData(ParseRuleStatus.Published)]
        public void Existing_validated_content_can_change_machines_without_a_sample(ParseRuleStatus status)
        {
            var version = new ParseRuleVersion { Status = status };

            Assert.False(MappingSavePolicy.RequiresSample(version, hasContentChanges: false));
        }

        [Theory]
        [InlineData(ParseRuleStatus.Draft, false)]
        [InlineData(ParseRuleStatus.Superseded, false)]
        [InlineData(ParseRuleStatus.Published, true)]
        public void Unvalidated_or_changed_content_still_requires_a_sample(
            ParseRuleStatus status,
            bool hasContentChanges)
        {
            var version = new ParseRuleVersion { Status = status };

            Assert.True(MappingSavePolicy.RequiresSample(version, hasContentChanges));
        }

        [Fact]
        public void A_new_mapping_requires_a_sample()
        {
            Assert.True(MappingSavePolicy.RequiresSample(null, hasContentChanges: false));
        }

        [Fact]
        public void Editor_prefers_published_version_over_newer_legacy_draft()
        {
            var published = new ParseRuleVersion
            {
                Id = 7,
                VersionNumber = 1,
                Status = ParseRuleStatus.Published
            };
            var legacyDraft = new ParseRuleVersion
            {
                Id = 8,
                VersionNumber = 2,
                Status = ParseRuleStatus.Draft
            };

            ParseRuleVersion selected = MappingSavePolicy.SelectPreferredEditorVersion(
                new List<ParseRuleVersion> { legacyDraft, published });

            Assert.Same(published, selected);
        }

        [Fact]
        public void Editor_uses_latest_draft_when_mapping_has_never_been_validated()
        {
            var firstDraft = new ParseRuleVersion
            {
                Id = 1,
                VersionNumber = 1,
                Status = ParseRuleStatus.Draft
            };
            var latestDraft = new ParseRuleVersion
            {
                Id = 2,
                VersionNumber = 2,
                Status = ParseRuleStatus.Draft
            };

            ParseRuleVersion selected = MappingSavePolicy.SelectPreferredEditorVersion(
                new List<ParseRuleVersion> { firstDraft, latestDraft });

            Assert.Same(latestDraft, selected);
        }
    }
}
