#!/usr/bin/env bash
# PySharpDB - Build and publish to PyPI
# Usage: bash publish.sh

set -euo pipefail

echo "PySharpDB - PyPI Publishing Script"
echo "=================================="

# Check if we're in the right directory
if [[ ! -f "pyproject.toml" ]]; then
    echo "ERROR: Run this script from the pysharpcoredb directory"
    exit 1
fi

# Check for required tools
if ! command -v python &> /dev/null; then
    echo "ERROR: Python not found"
    exit 1
fi

# --only-binary=:all: installs wheels only, so no setup.py from the index is executed at install time
# (SonarCloud S8541). Both build and twine publish wheels for every supported Python.
if ! python -c "import build" 2>/dev/null; then
    echo "Installing build tool..."
    python -m pip install --user --only-binary=:all: build
fi

if ! python -c "import twine" 2>/dev/null; then
    echo "Installing twine..."
    python -m pip install --user --only-binary=:all: twine
fi

# Clean previous builds
echo "• Cleaning previous builds..."
rm -rf dist/ build/ *.egg-info/

# Build the package
echo "• Building package..."
python -m build

# Verify the build
echo "• Verifying build..."
python -m twine check dist/*

# Show what was built
echo "• Build artifacts:"
ls -la dist/

echo ""
echo "📦 Package built successfully!"
echo ""
echo "To publish to PyPI:"
echo "  1. Test on TestPyPI first:"
echo "     python -m twine upload --repository testpypi dist/*"
echo "  2. Install from TestPyPI:"
echo "     pip install --index-url https://test.pypi.org/simple/ pysharpcoredb"
echo "  3. If tests pass, upload to real PyPI:"
echo "     python -m twine upload dist/*"
echo ""
echo "Note: You'll need PyPI credentials configured or set TWINE_USERNAME/TWINE_PASSWORD"
echo "For automated publishing, consider GitHub Actions with trusted publishing."
