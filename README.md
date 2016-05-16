## <a href="https://github.com/xunit/xunit"><img src="https://raw.github.com/xunit/media/master/full-logo.png" title="xUnit.net CoreCLR Runner" /></a>

This runner supports [xUnit.net](https://github.com/xunit/xunit) tests for [.NET 4.5.1+, and .NET Core 1.0+](https://github.com/dotnet/corefx) (this includes [ASP.NET Core 1.0+](https://github.com/aspnet)).

![](https://mseng.visualstudio.com/_apis/public/build/definitions/d09b7a4d-0a51-4c0e-a15a-07921d5b558f/3249/badge)

### Usage

To install this package, ensure your project.json contains the following lines:

```JSON
{
    "dependencies": {
        "xunit": "2.1.0",
        "dotnet-test-xunit": "1.0.0-*"
    },
    "testRunner": "xunit"
}
```
To use [xUnit configration](http://xunit.github.io/docs/configuring-with-json.html), make sure to add `buildOptions` to your project.json:

```JSON
"buildOptions": {
  "copyToOutput": {
    "include": [ "xunit.runner.json" ]
  }
}
```

To run tests from the command line, use the following.

```Shell
# Restore NuGet packages
dotnet restore

# Run tests in current directory
dotnet test

# Run tests if tests are not in the current directory
dotnet -p path/to/project test // not yet implemented
```

### More Information

For more complete example usage, please see [Getting Started with xUnit.net and CoreCLR / ASP.NET 5](http://xunit.github.io/docs/getting-started-coreclr.html).
