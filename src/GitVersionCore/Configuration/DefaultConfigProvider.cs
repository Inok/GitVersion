using System.Collections.Generic;
using System.Linq;
using GitVersion.Extensions;
using GitVersion.Model.Configuration;
using GitVersion.VersionCalculation;

namespace GitVersion.Configuration
{
    public class DefaultConfigProvider
    {
        private static readonly Dictionary<string, int> DefaultPreReleaseWeight =
            new Dictionary<string, int>
            {
                { Config.DevelopBranchRegex, 0 },
                { Config.HotfixBranchRegex, 30000 },
                { Config.ReleaseBranchRegex, 30000 },
                { Config.FeatureBranchRegex, 30000 },
                { Config.PullRequestRegex, 30000 },
                { Config.SupportBranchRegex, 55000 },
                { Config.MasterBranchRegex, 55000 }
            };

        private const IncrementStrategy DefaultIncrementStrategy = IncrementStrategy.Inherit;

        private const int DefaultTagPreReleaseWeight = 60000;

        public static void Reset(Config config)
        {
            config.AssemblyVersioningScheme ??= AssemblyVersioningScheme.MajorMinorPatch;
            config.AssemblyFileVersioningScheme ??= AssemblyFileVersioningScheme.MajorMinorPatch;
            config.AssemblyInformationalFormat = config.AssemblyInformationalFormat;
            config.AssemblyVersioningFormat = config.AssemblyVersioningFormat;
            config.AssemblyFileVersioningFormat = config.AssemblyFileVersioningFormat;
            config.TagPrefix ??= Config.DefaultTagPrefix;
            config.VersioningMode ??= VersioningMode.ContinuousDelivery;
            config.ContinuousDeploymentFallbackTag ??= "ci";
            config.MajorVersionBumpMessage ??= IncrementStrategyFinder.DefaultMajorPattern;
            config.MinorVersionBumpMessage ??= IncrementStrategyFinder.DefaultMinorPattern;
            config.PatchVersionBumpMessage ??= IncrementStrategyFinder.DefaultPatchPattern;
            config.NoBumpMessage ??= IncrementStrategyFinder.DefaultNoBumpPattern;
            config.CommitMessageIncrementing ??= CommitMessageIncrementMode.Enabled;
            config.LegacySemVerPadding ??= 4;
            config.BuildMetaDataPadding ??= 4;
            config.CommitsSinceVersionSourcePadding ??= 4;
            config.CommitDateFormat ??= "yyyy-MM-dd";
            config.UpdateBuildNumber ??= true;
            config.TagPreReleaseWeight ??= DefaultTagPreReleaseWeight;

            var configBranches = config.Branches.ToList();

            ApplyBranchDefaults(config, GetOrCreateBranchDefaults(config, Config.DevelopBranchKey), Config.DevelopBranchRegex,
                                new List<string>(),
                                defaultTag: "alpha",
                                defaultIncrementStrategy: IncrementStrategy.Minor,
                                defaultVersioningMode: config.VersioningMode == VersioningMode.Mainline ? VersioningMode.Mainline : VersioningMode.ContinuousDeployment,
                                defaultTrackMergeTarget: true,
                                tracksReleaseBranches: true);
            ApplyBranchDefaults(config, GetOrCreateBranchDefaults(config, Config.MasterBranchKey), Config.MasterBranchRegex,
                                new List<string>
                                { "develop", "release" },
                                defaultTag: string.Empty,
                                defaultPreventIncrement: true,
                                defaultIncrementStrategy: IncrementStrategy.Patch,
                                isMainline: true);
            ApplyBranchDefaults(config, GetOrCreateBranchDefaults(config, Config.ReleaseBranchKey), Config.ReleaseBranchRegex,
                                new List<string>
                                { "develop", "master", "support", "release" },
                                defaultTag: "beta",
                                defaultPreventIncrement: true,
                                defaultIncrementStrategy: IncrementStrategy.None,
                                isReleaseBranch: true);
            ApplyBranchDefaults(config, GetOrCreateBranchDefaults(config, Config.FeatureBranchKey), Config.FeatureBranchRegex,
                                new List<string>
                                { "develop", "master", "release", "feature", "support", "hotfix" },
                                defaultIncrementStrategy: IncrementStrategy.Inherit);
            ApplyBranchDefaults(config, GetOrCreateBranchDefaults(config, Config.PullRequestBranchKey), Config.PullRequestRegex,
                                new List<string>
                                { "develop", "master", "release", "feature", "support", "hotfix" },
                                defaultTag: "PullRequest",
                                defaultTagNumberPattern: @"[/-](?<number>\d+)",
                                defaultIncrementStrategy: IncrementStrategy.Inherit);
            ApplyBranchDefaults(config, GetOrCreateBranchDefaults(config, Config.HotfixBranchKey), Config.HotfixBranchRegex,
                                new List<string>
                                { "develop", "master", "support" },
                                defaultTag: "beta",
                                defaultIncrementStrategy: IncrementStrategy.Patch);
            ApplyBranchDefaults(config, GetOrCreateBranchDefaults(config, Config.SupportBranchKey), Config.SupportBranchRegex,
                                new List<string>
                                { "master" },
                                defaultTag: string.Empty,
                                defaultPreventIncrement: true,
                                defaultIncrementStrategy: IncrementStrategy.Patch,
                                isMainline: true);

            // Any user defined branches should have other values defaulted after known branches filled in.
            // This allows users to override any of the value.
            foreach (var (name, branchConfig) in configBranches)
            {
                var regex = branchConfig.Regex;
                if (regex == null)
                {
                    throw new ConfigurationException($"Branch configuration '{name}' is missing required configuration 'regex'{System.Environment.NewLine}" +
                                                     "See https://gitversion.net/docs/configuration/ for more info");
                }

                var sourceBranches = branchConfig.SourceBranches;
                if (sourceBranches == null)
                {
                    throw new ConfigurationException($"Branch configuration '{name}' is missing required configuration 'source-branches'{System.Environment.NewLine}" +
                                                     "See https://gitversion.net/docs/configuration/ for more info");
                }

                ApplyBranchDefaults(config, branchConfig, regex, sourceBranches);
            }

            // This is a second pass to add additional sources, it has to be another pass to prevent ordering issues
            foreach (var (name, branchConfig) in configBranches)
            {
                if (branchConfig.IsSourceBranchFor == null) continue;
                foreach (var isSourceBranch in branchConfig.IsSourceBranchFor)
                {
                    config.Branches[isSourceBranch].SourceBranches.Add(name);
                }
            }
        }

        private static BranchConfig GetOrCreateBranchDefaults(Config config, string branchKey)
        {
            if (!config.Branches.ContainsKey(branchKey))
            {
                var branchConfig = new BranchConfig { Name = branchKey };
                config.Branches.Add(branchKey, branchConfig);
                return branchConfig;
            }

            return config.Branches[branchKey];
        }

        public static void ApplyBranchDefaults(Config config,
                                               BranchConfig branchConfig,
                                               string branchRegex,
                                               List<string> sourceBranches,
                                               string defaultTag = "useBranchName",
                                               IncrementStrategy? defaultIncrementStrategy = null, // Looked up from main config
                                               bool defaultPreventIncrement = false,
                                               VersioningMode? defaultVersioningMode = null, // Looked up from main config
                                               bool defaultTrackMergeTarget = false,
                                               string defaultTagNumberPattern = null,
                                               bool tracksReleaseBranches = false,
                                               bool isReleaseBranch = false,
                                               bool isMainline = false)
        {
            branchConfig.Regex = string.IsNullOrEmpty(branchConfig.Regex) ? branchRegex : branchConfig.Regex;
            branchConfig.SourceBranches = branchConfig.SourceBranches == null || !branchConfig.SourceBranches.Any()
                                              ? sourceBranches
                                              : branchConfig.SourceBranches;
            branchConfig.Tag ??= defaultTag;
            branchConfig.TagNumberPattern ??= defaultTagNumberPattern;
            branchConfig.Increment ??= defaultIncrementStrategy ?? config.Increment ?? DefaultIncrementStrategy;
            branchConfig.PreventIncrementOfMergedBranchVersion ??= defaultPreventIncrement;
            branchConfig.TrackMergeTarget ??= defaultTrackMergeTarget;
            branchConfig.VersioningMode ??= defaultVersioningMode ?? config.VersioningMode;
            branchConfig.TracksReleaseBranches ??= tracksReleaseBranches;
            branchConfig.IsReleaseBranch ??= isReleaseBranch;
            branchConfig.IsMainline ??= isMainline;
            DefaultPreReleaseWeight.TryGetValue(branchRegex, out var defaultPreReleaseNumber);
            branchConfig.PreReleaseWeight ??= defaultPreReleaseNumber;
        }
    }
}