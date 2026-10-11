"""pytest configuration and shared fixtures."""
import sys
import os

# Make sure CLI package is importable from tests
sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", "src", "cli"))
