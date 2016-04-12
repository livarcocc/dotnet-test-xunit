#!/bin/bash

echo ""
echo "Installing dotnet cli..."
echo ""

export DOTNET_INSTALL_DIR="./.dotnet/"

tools/install.sh

origPath=$PATH
export PATH="./dotnet/bin/:$PATH"

if [ $? -ne 0 ]; then
  echo >&2 ".NET Execution Environment installation has failed."
  exit 1
fi

export DOTNET_HOME="$DOTNET_INSTALL_DIR/cli"
export PATH="$DOTNET_HOME/bin:$PATH"

export autoGeneratedVersion=false

# Generate version number if not set
if [[ -z "$BuildSemanticVersion" ]]; then
    autoVersion="$((($(date +%s) - 1451606400)/60))-$(date +%S)"
    export BuildSemanticVersion="rc2-$autoVersion"
    autoGeneratedVersion=true
    
    echo "Set version to $BuildSemanticVersion"
fi

sed -i '' "s/99.99.99-rc2/1.0.0-$BuildSemanticVersion/g" */*/project.json 

# Restore packages and build product
dotnet restore "src/dotnet-test-xunit"
dotnet pack "src/dotnet-test-xunit" --configuration Release --output "artifacts/packages"

#restore, compile, and run tests
dotnet restore "test" -s "artifacts/packages"
for test in test/*/
do 
echo "Testing $test"
    pushd "$test"
    dotnet build
    dotnet test
    popd
done

sed -i '' "s/1.0.0-$BuildSemanticVersion/99.99.99-rc2/g" */*/project.json 

if [ $autoGeneratedVersion ]; then
    unset BuildSemanticVersion
fi

export PATH=$origPath
