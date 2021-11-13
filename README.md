# AsyncPropagationRefactoring

Roslyn Code Refactoring, which provides sync to async method conversion with propagation across method call-chain and inheritance chain.
Also supports reverse async to sync conversion

## Motivation

## Features
## Limitations

## Getting started

### Omnisharp (VSCode)

You need to point Omnisharp to folder, containing AsyncPropagationRefactoring.dll using [omnisharp.json](https://github.com/OmniSharp/omnisharp-roslyn/wiki/Configuration-Options#roslyn-extensions-options).
For example, if AsyncPropagationRefactoring is installed like this: `nuget install AsyncPropagationRefactoring -o c:\refactorings`, then config section of omnisharp.json should look like this:
```json
{
    "RoslynExtensionsOptions": {
        "locationPaths": [
            "C:\\refactorings\\AsyncPropagationRefactoring.0.2.0\\lib\\netstandard2.0"
        ]
    }
}
```
omnisharp.json can be placed at the root of your solution, or globally into `%USERPROFILE%/.omnisharp/`.
After reloading OmniSharp you can use refactoring on methods through `ctrl+.` context menu.

### Visual Studio
#### Per-project installation
Visual Studio have support for loading refactorings from assemblies, added to project. 
This way, AsyncPropagationRefactoring nuget package can be installed into project and become available through `ctrl+.` context menu **for this project**.