# Build Unity DLLs with the .NET SDK

Run the following in the UniTask repository root. The projects default to Unity
6000.3.17 and .NET Standard 2.1. Set `UnityVersion` to the numeric version of the
Unity installation you reference, without its release suffix (`f1`, etc.).

```powershell
dotnet build UniTask.Editor.csproj -c Release `
  '-p:UnityData=C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data' `
  '-p:UnityVersion=6000.3.17' `
  '-p:UnityUGuiAssembly=C:/path/to/UnityProject/Library/ScriptAssemblies/UnityEngine.UI.dll'
```

`UnityData` can also be set through the `UNITY_EDITOR_DATA` environment variable.
On macOS, use the Unity application's `Contents` directory. `UnityUGuiAssembly`
is optional: when omitted, uGUI extensions are excluded. Other optional Unity
modules are enabled when their assemblies are present in the installation.

For persistent local settings, create `UniTask.Unity.local.props` (ignored by Git):

```xml
<Project>
  <PropertyGroup>
    <UnityData>C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data</UnityData>
    <UnityVersion>6000.3.17</UnityVersion>
    <UnityUGuiAssembly>C:/path/to/UnityProject/Library/ScriptAssemblies/UnityEngine.UI.dll</UnityUGuiAssembly>
  </PropertyGroup>
</Project>
```

Building `UniTask.Editor.csproj` also builds `UniTask.csproj`. Both include
`UNITY_EDITOR`, so task tracking and Editor lifecycle hooks are available.
Outputs are `bin/UniTask/Editor/Release/netstandard2.1/UniTask.dll` and
`bin/UniTask.Editor/Editor/Release/netstandard2.1/UniTask.Editor.dll`.

To compile the runtime for a Player without Editor dependencies:

```powershell
dotnet build UniTask.csproj -c Release -p:UniTaskBuildForEditor=false
```

Supply the same Unity reference settings as above, or use the local props file.
This Player variant uses the Mono scripting backend. The output is
`bin/UniTask/Player/Release/netstandard2.1/UniTask.dll`. Configure
Unity's Plugin Inspector so Editor and Player variants load only in their intended
environments, and remove the corresponding source assembly before using its DLL.
These commands compile the libraries; they do not execute Unity's runtime tests.

The source boundaries match the existing assembly definitions: `UniTask.csproj`
includes `Runtime` except `Linq` and `External`, and `UniTask.Editor.csproj` includes
`Editor`. Linq, Addressables, DOTween and TextMeshPro remain separate assemblies.
The existing NetCore build and tests use their own solution explicitly:

```powershell
dotnet test UniTask.NetCore.sln -c Release
```
