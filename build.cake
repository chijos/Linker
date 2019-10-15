#tool nuget:?package=GitVersion.CommandLine&version=5.0.1
#tool nuget:?package=OctopusTools&version=6.7.0

#addin nuget:?package=Cake.Npm&version=0.17.0
#addin nuget:?package=Cake.Curl&version=4.1.0

#load build/path.cake
#load build/package.cake
#load build/url.cake

var target = Argument("Target", "Compile");
var deployTo = Argument("DeployTo", "Test");
var zipDeploymentUri = new Uri(Argument<string>("ZipDeploymentUri"));

Setup<PackageMetadata>(context => {
    var metadata = new PackageMetadata(
        outputDirectory: Argument("PackageOutputDirectory", "dist"),
        name: "Linker"
    );
    Information($"Package:\n{metadata}");
    return metadata;
});

Task("Compile")
    .Does(() => DotNetCoreBuild(Paths.SolutionFile.FullPath));

Task("Test")
    .IsDependentOn("Compile")
    .Does(() => DotNetCoreTest(Paths.TestProjectFile.FullPath));

Task("Version")
    .Does<PackageMetadata>(package => {
        package.Version = GitVersion().FullSemVer;
        Information($"Version number: {package.Version}");
    });

Task("Build-Frontend")
    .Does(() => {
        NpmInstall(settings => 
            settings.FromPath(Paths.WebProjectDirectory));

        NpmRunScript(
            "build", 
            settings => settings.FromPath(Paths.WebProjectDirectory));
    });

Task("Package-Zip")
    .IsDependentOn("Test")
    .IsDependentOn("Build-FrontEnd")
    .IsDependentOn("Version")
    .Does<PackageMetadata>(package => {
        package.Extension = "zip";

        CleanDirectory(package.OutputDirectory);

        DotNetCorePublish(
            Paths.WebProjectFile.FullPath, 
            new DotNetCorePublishSettings {
                OutputDirectory = Paths.PublishDirectory.FullPath,
                NoRestore = true,
                NoBuild = true
            });

        EnsureDirectoryExists(package.OutputDirectory);

        Zip(Paths.PublishDirectory.FullPath, package.FullPath);
    });

Task("Package-Octopus")
    .IsDependentOn("Test")
    .IsDependentOn("Build-FrontEnd")
    .IsDependentOn("Version")
    .Does<PackageMetadata>(package => {
        CleanDirectory(package.OutputDirectory);

        package.Extension = "nupkg";

        DotNetCorePublish(
            Paths.WebProjectFile.FullPath, 
            new DotNetCorePublishSettings {
                OutputDirectory = Paths.PublishDirectory.FullPath,
                NoRestore = true,
                NoBuild = true
            });

        OctoPack(
            package.Name,
            new OctopusPackSettings
            {
                BasePath = Paths.PublishDirectory,
                OutFolder = package.OutputDirectory,
                Version = package.Version,
                Format = OctopusPackFormat.NuPkg
            });
    });

Task("Deploy-Zip")
    .IsDependentOn("Package-Zip")
    .Does<PackageMetadata>(package => {
        CurlUploadFile(
            package.FullPath, 
            zipDeploymentUri,
            new CurlSettings
            {
                ArgumentCustomization = args => args.Append("--fail"),
                Username = EnvironmentVariable("DeploymentUser"),
                Password = EnvironmentVariable("DeploymentPassword")
            });
    });

Task("Deploy-Octopus")
    .IsDependentOn("Package-Octopus")
    .Does<PackageMetadata>(package => {

        const string apiKey = "API-W09OVPHTQA2JO7KEV5WAVFJY3W";

        OctoPush(
            Urls.OctopusDeploymentEndpoint.AbsoluteUri, 
            apiKey,
            package.FullPath,
            new OctopusPushSettings 
            { 
                EnableServiceMessages = true,
                ReplaceExisting = true
            });

        OctoCreateRelease(
            "Linker-1",
            new CreateReleaseSettings
            {
                Server = Urls.OctopusDeploymentEndpoint.AbsoluteUri,
                ApiKey = apiKey,
                ReleaseNumber = package.Version,
                DefaultPackageVersion = package.Version,
                IgnoreExisting = true
            });

        OctoDeployRelease(
            Urls.OctopusDeploymentEndpoint.AbsoluteUri,
            apiKey,
            "Linker-1",
            deployTo,
            package.Version,
            new OctopusDeployReleaseDeploymentSettings
            {
                EnableServiceMessages = true,
                WaitForDeployment = true
            });
    });

Task("Set-Build-Number")
    .IsDependentOn("Version")
    .WithCriteria(BuildSystem.IsRunningOnTeamCity)
    .Does<PackageMetadata>(package => {
        TeamCity.SetBuildNumber(package.Version);
    });

Task("Publish-Build-Artifact")
    .IsDependentOn("Package-Zip")
    .WithCriteria(BuildSystem.IsRunningOnTeamCity)
    .Does<PackageMetadata>(package => {
        TeamCity.PublishArtifacts(package.FullPath);
    });

Task("Deploy-CI")
    .IsDependentOn("Deploy-Zip")
    .IsDependentOn("Set-Build-Number")
    .IsDependentOn("Publish-Build-Artifact");

RunTarget(target);