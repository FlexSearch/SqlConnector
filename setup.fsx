// include Fake lib
#r @"src\packages\FAKE\tools\FakeLib.dll"
#r "System.Management.Automation"
#load @"lib\src\Helpers.fsx"

open Fake
open Fake.AssemblyInfoFile
open System.IO
open System.Linq
open System
open System.Diagnostics
open System.Management.Automation
open Helpers

//TraceEnvironmentVariables()

Target "RestorePackages" (fun _ ->
    !! "./src/**/packages.config"
        |> Seq.iter (RestorePackage (fun p ->
            { p with
                OutputPath = "./src/packages"
                Sources = [@"https://nuget.org/api/v2/"; @"https://www.myget.org/F/aspnetvnext/api/v2/"]}))
)

if buildServer = BuildServer.AppVeyor then 
    MSBuildLoggers <- @"""C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll""" :: MSBuildLoggers

// Version information
let majorVersion = 0
let minorVersion = 9
let patchLevel = 2
let buildVersion = System.DateTime.UtcNow.ToString("yyyyMMddhhmm")
let version = sprintf "%i.%i.%i-alpha+%s" majorVersion minorVersion patchLevel buildVersion
let productName = "FlexSearch"
let copyright = sprintf "Copyright (C) 2010 - %i - FlexSearch" DateTime.Now.Year


// Create necessary directories if they don't exist
ensureDirectory(buildDir)
ensureDirectory(testDir)
ensureDirectory(deployDir)

let AssemblyInfo path title = 
    CreateFSharpAssemblyInfo (sprintf @".\src\%s\AssemblyInfo.fs" path) [ Attribute.Title title
                                                                          Attribute.Description title
                                                                          Attribute.Product productName
                                                                          Attribute.Copyright copyright
                                                                          Attribute.FileVersion version
                                                                          Attribute.Version version ]

let runPsScript scriptText =
    let ps = PowerShell.Create()
    let result = ps.AddScript(scriptText).Invoke()
        
    trace "PS Script Output:\n"
    result |> Seq.iter (sprintf "%A" >> trace)
    if result.Count > 0 then
        // Last exit code
        if (result |> Seq.last).ToString() <> "0" then 
            failwith "The powershell script exited with a non-success code. Please check previous error messages for details."

    if (ps.Streams.Error.Count > 0) then
        trace "PS Script non-fatal errors:\n"
        ps.Streams.Error |> Seq.iter (sprintf "%A" >> trace)

// Targets
Target "Clean" <| fun _ -> CleanDirs [ buildDir; testDir; deployDir ]
Target "BuildApp" <| fun _ -> 
    AssemblyInfo "SqlConnector" "FlexSearch SQL Connector"
    MSBuildRelease buildDir "Build" [ @"src\SqlConnector.sln" ] |> Log "BuildApp-Output: "
    // Copy the files from build to build-test necessary for Testing
    FileHelper.CopyRecursive buildDir testDir true |> ignore

Target "Test" (fun _ -> 
    !! (testDir @@ "SqlConnector.Tests.dll") 
    |> (fun includes ->
            try FixieHelper.Fixie 
                    (fun p -> { p with CustomOptions = [ "xUnitXml", "TestResult.xml" :> obj ] })
                    includes
            // Upload test results to Appveyor even if tests failed
            finally AppVeyor.UploadTestResultsXml AppVeyor.TestResultsType.Xunit __SOURCE_DIRECTORY__
                    trace "Uploaded to AppVeyor"))
            
Target "Zip" <| fun _ -> 
    !! (buildDir + "/**/SqlConnector.dll")
    ++ (buildDir + "/sqlconnector/**/*.*")
    |> Zip buildDir (deployDir <!!> "SqlConnector." + version + ".zip")

// Portal related
Target "BuildPortal" <| fun _ ->
    trace "Copying the portal files to the portal build directory"
    FileHelper.CopyRecursive basePortalDir portalBuildDir true |> ignore
    ensureDirectory (portalBuildDir <!!> "src/apps/sqlconnector")
    FileHelper.CopyRecursive portalDir (portalBuildDir <!!> "src/apps/sqlconnector") true |> ignore

    FileUtils.cd portalBuildDir

    runPsScript <| File.ReadAllText "build.ps1"

    FileUtils.cd rootDir

Target "DeployPortal" <| fun _ ->
    ensureDirectory (buildDir <!!> "sqlconnector")
    let baseSource = (portalBuildDir <!!> "src/apps/sqlconnector/dist")
    !! (baseSource + "/**/*.*") 
    |> Seq.iter (FileHelper.CopyFileWithSubfolder baseSource (buildDir <!!> "sqlconnector"))

// Dependencies
"Clean" 
==> "RestorePackages" 
==> "BuildApp"
==> "DeployPortal"
==> "Zip"
==> "Test"

"BuildPortal"
==> "DeployPortal"


// start building core FlexSearch
RunTargetOrDefault "Zip"