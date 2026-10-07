"""Compatibility entry point for the current comprehensive integration suite."""
from pathlib import Path
import subprocess, sys
subprocess.run([sys.executable, str(Path(__file__).with_name("test_optional.py"))], check=True)
