#!/bin/bash

# Test State Isolation Verification Script
# This script verifies the TestClassFactory implementation without running full tests

set -e

echo "╔═══════════════════════════════════════════════════════════════════════════════╗"
echo "║                Test State Isolation Verification Script                       ║"
echo "╚═══════════════════════════════════════════════════════════════════════════════╝"
echo ""

# Colors for output
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

ERRORS=0

# Function to print status
print_status() {
    if [ $1 -eq 0 ]; then
        echo -e "${GREEN}✓${NC} $2"
    else
        echo -e "${RED}✗${NC} $2"
        ((ERRORS++))
    fi
}

print_info() {
    echo -e "${YELLOW}ℹ${NC} $1"
}

echo "1. Checking Prerequisites..."
echo "─────────────────────────────────────────────────────────────────────────────────"

# Check .NET SDK
if command -v dotnet &> /dev/null; then
    DOTNET_VERSION=$(dotnet --version)
    print_status 0 ".NET SDK found: $DOTNET_VERSION"

    # Check if it's .NET 10 or compatible
    if [[ "$DOTNET_VERSION" =~ ^10\. ]]; then
        print_status 0 ".NET 10 SDK detected"
    else
        print_info "Note: .NET version is $DOTNET_VERSION (expected 10.x)"
    fi
else
    print_status 1 ".NET SDK not found"
    echo "   Install from: https://dotnet.microsoft.com/download"
fi

# Check Docker
if command -v docker &> /dev/null; then
    print_status 0 "Docker found: $(docker --version)"

    # Check if Docker daemon is running
    if docker ps &> /dev/null; then
        print_status 0 "Docker daemon is running"
    else
        print_status 1 "Docker daemon is not running"
        echo "   Start with: sudo systemctl start docker"
    fi
else
    print_status 1 "Docker not found"
    echo "   Install from: https://docs.docker.com/get-docker/"
fi

echo ""
echo "2. Verifying File Structure..."
echo "─────────────────────────────────────────────────────────────────────────────────"

# Check TestClassFactory exists
if [ -f "SharpMUSH.Tests/TestClassFactory.cs" ]; then
    print_status 0 "TestClassFactory.cs exists"

    # Check key components in the file
    # TestClassFactory uses PerTestSession for TestContainers (shared across all test classes)
    # Test classes use PerClass when injecting TestClassFactory (one factory per test class)
    if grep -q "SharedType.PerTestSession" "SharpMUSH.Tests/TestClassFactory.cs"; then
        print_status 0 "Uses PerTestSession for TestContainers (correct)"
    else
        print_status 1 "PerTestSession for TestContainers not found"
    fi

    if grep -q "GenerateDatabaseName" "SharpMUSH.Tests/TestClassFactory.cs"; then
        print_status 0 "Database name generation implemented"
    else
        print_status 1 "Database name generation not found"
    fi

    if grep -q "ClassDataSource<.*TestServer>" "SharpMUSH.Tests/TestClassFactory.cs"; then
        print_status 0 "TestContainer injection configured"
    else
        print_status 1 "TestContainer injection not found"
    fi
else
    print_status 1 "TestClassFactory.cs not found"
fi

# Check WebAppFactory is deprecated
if [ -f "SharpMUSH.Tests/WebAppFactory.cs" ]; then
    print_status 0 "WebAppFactory.cs exists (for reference)"

    if grep -q "DEPRECATED" "SharpMUSH.Tests/WebAppFactory.cs"; then
        print_status 0 "WebAppFactory marked as DEPRECATED (in comments)"
    else
        print_status 1 "WebAppFactory not marked as deprecated"
    fi
else
    print_status 1 "WebAppFactory.cs not found"
fi

echo ""
echo "3. Verifying Test Class Migration..."
echo "─────────────────────────────────────────────────────────────────────────────────"

# Count test classes using TestClassFactory
TESTCLASSFACTORY_COUNT=$(grep -r "ClassDataSource<TestClassFactory>" SharpMUSH.Tests/ --include="*.cs" | wc -l)
print_info "Found $TESTCLASSFACTORY_COUNT test classes using TestClassFactory"

# Count test classes using WebAppFactory (should be 0 except WebAppFactory.cs itself)
WEBAPPFACTORY_COUNT=$(grep -r "ClassDataSource<WebAppFactory>" SharpMUSH.Tests/ --include="*.cs" | grep -v "WebAppFactory.cs" | wc -l)
if [ "$WEBAPPFACTORY_COUNT" -eq 0 ]; then
    print_status 0 "No test classes using deprecated WebAppFactory"
else
    print_status 1 "$WEBAPPFACTORY_COUNT test classes still using WebAppFactory"
fi

# Check for WebAppFactoryArg references (should be 0)
WEBAPPFACTORYARG_COUNT=$(grep -r "WebAppFactoryArg" SharpMUSH.Tests/ --include="*.cs" | grep -v "WebAppFactory.cs" | wc -l)
if [ "$WEBAPPFACTORYARG_COUNT" -eq 0 ]; then
    print_status 0 "No WebAppFactoryArg references remaining"
else
    print_status 1 "$WEBAPPFACTORYARG_COUNT WebAppFactoryArg references still exist"
    echo "   Files with WebAppFactoryArg:"
    grep -r "WebAppFactoryArg" SharpMUSH.Tests/ --include="*.cs" -l | grep -v "WebAppFactory.cs" | head -5
fi

# Check for NotInParallel at class level (should be minimal)
NOTINPARALLEL_COUNT=$(grep -r "^\[NotInParallel\]" SharpMUSH.Tests/ --include="*.cs" | wc -l)
print_info "Found $NOTINPARALLEL_COUNT [NotInParallel] attributes (expected: few or none)"

echo ""
echo "4. Checking Key Test Classes..."
echo "─────────────────────────────────────────────────────────────────────────────────"

# Check HelpCommandTests (proof of concept)
if [ -f "SharpMUSH.Tests/Commands/HelpCommandTests.cs" ]; then
    if grep -q "TestClassFactory" "SharpMUSH.Tests/Commands/HelpCommandTests.cs"; then
        print_status 0 "HelpCommandTests migrated"
    else
        print_status 1 "HelpCommandTests not migrated"
    fi
else
    print_status 1 "HelpCommandTests.cs not found"
fi

# Check BuildingCommandTests (had DependsOn)
if [ -f "SharpMUSH.Tests/Commands/BuildingCommandTests.cs" ]; then
    if grep -q "TestClassFactory" "SharpMUSH.Tests/Commands/BuildingCommandTests.cs"; then
        print_status 0 "BuildingCommandTests migrated"
    else
        print_status 1 "BuildingCommandTests not migrated"
    fi

    # Check if DependsOn<GeneralCommandTests> was handled
    if grep -q "DependsOn<GeneralCommandTests>" "SharpMUSH.Tests/Commands/BuildingCommandTests.cs"; then
        print_info "BuildingCommandTests still has cross-class DependsOn (may need review)"
    fi
else
    print_status 1 "BuildingCommandTests.cs not found"
fi

# Check AttributeFunctionUnitTests (had NotInParallel)
if [ -f "SharpMUSH.Tests/Functions/AttributeFunctionUnitTests.cs" ]; then
    if grep -q "TestClassFactory" "SharpMUSH.Tests/Functions/AttributeFunctionUnitTests.cs"; then
        print_status 0 "AttributeFunctionUnitTests migrated"
    else
        print_status 1 "AttributeFunctionUnitTests not migrated"
    fi
else
    print_status 1 "AttributeFunctionUnitTests.cs not found"
fi

echo ""
echo "5. Attempting Build..."
echo "─────────────────────────────────────────────────────────────────────────────────"

if command -v dotnet &> /dev/null; then
    print_info "Building test project..."

    if dotnet build SharpMUSH.Tests/SharpMUSH.Tests.csproj --nologo --verbosity quiet > /tmp/build.log 2>&1; then
        print_status 0 "Build succeeded"
    else
        print_status 1 "Build failed"
        echo ""
        echo "Build errors (last 20 lines):"
        tail -20 /tmp/build.log
    fi
else
    print_info "Skipping build (dotnet not available)"
fi

echo ""
echo "═════════════════════════════════════════════════════════════════════════════════"

if [ $ERRORS -eq 0 ]; then
    echo -e "${GREEN}✓ All checks passed!${NC}"
    echo ""
    echo "Next steps:"
    echo "  1. Run a single test class:      dotnet test --filter \"FullyQualifiedName~HelpCommandTests\""
    echo "  2. Verify container count:       docker ps | grep -E \"arango|mysql|redpanda|prometheus|redis\""
    echo "  3. Run full test suite:          dotnet test SharpMUSH.Tests/SharpMUSH.Tests.csproj"
    echo ""
    echo "See TESTING_VERIFICATION.md for detailed verification instructions."
    exit 0
else
    echo -e "${RED}✗ $ERRORS check(s) failed${NC}"
    echo ""
    echo "Please address the issues above before running tests."
    echo "See TESTING_VERIFICATION.md for troubleshooting guidance."
    exit 1
fi
