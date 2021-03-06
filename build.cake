#tool "nuget:?package=GitVersion.CommandLine"

//////////////////////////////////////////////////////////////////////
// CONFIGURATIONS
//////////////////////////////////////////////////////////////////////

var versionPropsTemplate = "./Version.props.template";
var versionProps = "./../Version.props";
var nugetSources = new[] {"https://nuget.sahbdev.dk/nuget", "https://api.nuget.org/v3/index.json"};

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
var configuration = Argument("configuration", "Release");
var sln = Argument("sln", "");
var nugetPushSource = Argument("nuget_push_source", "https://nuget.sahbdev.dk/nuget");
var nugetAPIKey = Argument("nuget_push_apikey", "");

//////////////////////////////////////////////////////////////////////
// Solution
//////////////////////////////////////////////////////////////////////

if (string.IsNullOrEmpty(sln)) {
	sln = System.IO.Directory.GetFiles("..", "*.sln")[0];
}

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    CleanDirectories("./../src/**/bin");
	CleanDirectories("./../src/**/obj");
	CleanDirectories("./../tests/**/bin");
	CleanDirectories("./../tests/**/obj");
});

GitVersion versionInfo = null;
Task("Version")
    .Does(() => 
{
	GitVersion(new GitVersionSettings{
		UpdateAssemblyInfo = false,
		OutputType = GitVersionOutput.BuildServer,
		WorkingDirectory = ".."
	});
	versionInfo = GitVersion(new GitVersionSettings{
		UpdateAssemblyInfo = false,
		OutputType = GitVersionOutput.Json,
		WorkingDirectory = ".."
	});
		
	// Update version
	var updatedVersionProps = System.IO.File.ReadAllText(versionPropsTemplate)
		.Replace("1.0.0", versionInfo.NuGetVersion);

	System.IO.File.WriteAllText(versionProps, updatedVersionProps);
});

Task("ReleaseNotes")
	.Does(() =>
{
	using(var process = StartAndReturnProcess("git", new ProcessSettings { Arguments = "log --pretty=%s --first-parent", RedirectStandardOutput = true })) {
		process.WaitForExit();
		
		System.IO.File.WriteAllText("../releasenotes.md", "# " + versionInfo.NuGetVersion + "\n");
		
		System.IO.File.AppendAllLines("../releasenotes.md", process.GetStandardOutput());
	}
});

Task("Restore-NuGet-Packages")
    .Does(() =>
{
	var settings = new DotNetCoreRestoreSettings 
    {
		Sources = nugetSources
    };

    DotNetCoreRestore(sln, settings);
});

Task("Build")
	.IsDependentOn("Clean")
	.IsDependentOn("Version")
	.IsDependentOn("ReleaseNotes")
	.IsDependentOn("Restore-NuGet-Packages")
    .Does(() =>
{
	var settings = new DotNetCoreBuildSettings
    {
		Configuration = configuration
    };

	DotNetCoreBuild(sln, settings);
});

Task("Publish")
	.IsDependentOn("Build")
	.Does(() =>
{
	var settings = new DotNetCorePublishSettings
    {
		Configuration = configuration
    };

	DotNetCorePublish(sln, settings);
});

Task("Test-CI")
    .Does(() =>
{
	foreach (var test in System.IO.Directory.GetFiles("../tests/", "*.Tests.csproj", SearchOption.AllDirectories))
	{
		var settings = new DotNetCoreTestSettings
		{
			Configuration = configuration,
			NoBuild = true,
			ArgumentCustomization = args=>args.Append("--logger \"trx;LogFileName=TestResults.trx\""),
		};
	
		DotNetCoreTest(test, settings);
	}
});

Task("Test")
	.IsDependentOn("Build")
    .IsDependentOn("Test-CI");

Task("NugetPush-CI")
	.Does(() =>
{
	var settings = new NuGetPushSettings
	{
		Source = nugetPushSource,
		ApiKey = nugetAPIKey
	};
	
	var packages =  GetFiles("./../src/**/*.nupkg");
	
	NuGetPush(packages, settings);
});

Task("NugetPush")
	.IsDependentOn("Test")
	.IsDependentOn("NugetPush-CI");

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    .IsDependentOn("Test");

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
