using System;
using System.Collections.Generic;
using System.Linq;

using JetBrains.Annotations;

using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using Nuke.Components;

using Serilog;

using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;

[GitHubActions(
    "ci",
    GitHubActionsImage.UbuntuLatest,
    AutoGenerate = false,
    FetchDepth = 0,
    OnPushBranches = ["*"],
    OnPullRequestBranches = [ "*" ],
    InvokedTargets = [nameof(ITest.Test), nameof(IPack.Pack)],
    EnableGitHubToken = true,
    PublishArtifacts = false

    // ImportSecrets = new[] { nameof(FeedzNuGetApiKey) })
    
    )]
class Build : NukeBuild
{

    
    // IEnumerable<Project> ITest.TestProjects => Partition.GetCurrent(Solution.GetAllProjects("*.Specs"));
    
    public static int Main () => Execute<Build>(x => x.Test);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    
    [Solution(GenerateProjects = true)] readonly Solution Solution;
    
    [Required]
    [GitVersion(Framework = "net8.0", NoCache = true, NoFetch = true)]
    readonly GitVersion GitVersion;

    [Required]
    [GitRepository]
    readonly GitRepository GitRepository;
    
    AbsolutePath ArtifactsDirectory => RootDirectory / "Artifacts";

    AbsolutePath TestResultsDirectory => RootDirectory / "TestResults";

    string SemVer;

    [CanBeNull] GitHubActions GitHubActions => GitHubActions.Instance;
    
    [Parameter] [Secret] readonly string PublicNuGetApiKey;
    [Parameter] [Secret] readonly string GitHubToken;

    Target Print => _ => _
        .Executes(() =>
        {
            if (GitHubActions is not null)
            {
                Log.Information("Branch = {Branch}", GitHubActions.Ref);
                Log.Information("Commit = {Commit}", GitHubActions.Sha);
            }
        });

    Target Clean => _ => _
        .DependsOn(Print)
        .Executes(() =>
        {
            ArtifactsDirectory.CreateOrCleanDirectory();
            TestResultsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetTasks.DotNetRestore();
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetTasks.DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTasks.DotNetTest(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .SetResultsDirectory(TestResultsDirectory)
                .SetProcessEnvironmentVariable("TELEMETRY_OPTOUT", "true"));
        });
    
    Target Pack => _ => _ 
        .DependsOn(Compile)
        .DependsOn(Test)
        // .Requires(() => Configuration == Configuration.Release)
        .Executes(() =>
        {
            DotNetTasks.DotNetPack(s => s
                .SetProject(Solution)
                // .SetConfiguration(Configuration)
                .SetOutputDirectory(ArtifactsDirectory)
                .SetVersion(GitVersion.NuGetVersionV2)
                // .EnableNoBuild())
              );
        });
    
    Target Publish => _ => _
        .DependsOn(Pack)
        .Executes(() =>
        {
        });
}
