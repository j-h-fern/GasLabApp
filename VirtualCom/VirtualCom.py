import subprocess
import shutil
import sys

# --- CONFIG ---
# Desired Windows-visible COM port names. Make sure these are free.
COM_A = "COM10"
COM_B = "COM11"

# If setupc.exe is not in PATH, specify the absolute path here:
SETUPC_PATH = shutil.which("setupc.exe") or r"C:\Program Files (x86)\com0com\setupc.exe"

def run(cmd):
    print(">", " ".join(cmd))
    completed = subprocess.run(cmd, capture_output=True, text=True, shell=False)
    if completed.returncode != 0:
        print(completed.stdout)
        print(completed.stderr, file=sys.stderr)
        raise SystemExit(f"Command failed with code {completed.returncode}")
    if completed.stdout.strip():
        print(completed.stdout)
    return completed

def ensure_setupc():
    if not SETUPC_PATH or not shutil.which(SETUPC_PATH) and not shutil.which("setupc.exe"):
        raise SystemExit("setupc.exe not found. Install com0com and/or update SETUPC_PATH.")
    return SETUPC_PATH

def list_pairs():
    setupc = ensure_setupc()
    out = run([setupc, "list"])
    return out

def create_pair(com_a=COM_A, com_b=COM_B):
    """
    Create a pair with the exact COM names exposed (use --emur to set the friendly names).
    """
    setupc = ensure_setupc()
    # Create the pair
    run([setupc, "install", "PortName=COMA", "PortName=COMB"])
    # Find the latest pair symbolic names (CNCAx/CNCBx)
    # We’ll set the Windows-visible names to COM_A / COM_B:
    run([setupc, "emur", "COMA", f"PortName={com_a}"])
    run([setupc, "emur", "COMB", f"PortName={com_b}"])
    print(f"Created virtual pair {com_a} <-> {com_b}")

def remove_pair_by_names(com_a=COM_A, com_b=COM_B):
    """
    Removes the pair that exposes the given COM names.
    """
    setupc = ensure_setupc()
    # 'list' output contains mapping; we'll try a best-effort removal by searching
    res = subprocess.run([setupc, "list"], capture_output=True, text=True)
    text = res.stdout

    # Find the CNCA*/CNCB* device names that correspond to our COMs
    import re
    # Example lines contain 'CNCA0 PortName=COM10' and 'CNCB0 PortName=COM11'
    cnca = re.findall(r"(CNC A\d+|CNCA\d+)\s+PortName=" + re.escape(com_a), text)
    cncb = re.findall(r"(CNC B\d+|CNCB\d+)\s+PortName=" + re.escape(com_b), text)

    # Fallback: try global cleanup if specific names not found
    if not cnca or not cncb:
        print("Could not match pair by COM names; attempting a generic remove of any null-modem pair with those names...")
    # com0com uses paired removal with 'remove <pair>' but the CLI variants differ across builds.
    # The simplest safe approach is to reset emur names, then remove any pair that still references them.
    # Try direct remove of all pairs where either side matches our COM name:
    lines = [l.strip() for l in text.splitlines()]
    pair_ids = []
    for i, line in enumerate(lines):
        # Pairs look like 'CNCA0<->CNCB0' on some builds
        if "<->" in line:
            left, right = [p.strip() for p in line.split("<->", 1)]
            pair_ids.append((left.split()[0], right.split()[0]))
    removed = 0
    for left, right in pair_ids:
        # Inspect details
        det = subprocess.run([setupc, "list", left], capture_output=True, text=True)
        if com_a in det.stdout or com_b in det.stdout:
            print(f"Removing pair {left} <-> {right}")
            run([setupc, "remove", left])
            run([setupc, "remove", right])
            removed += 1
    if removed == 0:
        print("No matching pairs removed. You may open com0com Setup and remove manually.")
    else:
        print(f"Removed {removed} pair(s) exposing {com_a}/{com_b}")

if __name__ == "__main__":
    import argparse
    p = argparse.ArgumentParser(description="Create or remove a com0com virtual COM pair")
    p.add_argument("--create", action="store_true", help="Create the virtual pair")
    p.add_argument("--remove", action="store_true", help="Remove the virtual pair")
    p.add_argument("--coma", default=COM_A, help="First COM port name (default COM10)")
    p.add_argument("--comb", default=COM_B, help="Second COM port name (default COM11)")
    args = p.parse_args()

    if not (args.create or args.remove):
        p.error("Specify --create or --remove")

    if args.create:
        create_pair(args.coma, args.comb)
        list_pairs()
    if args.remove:
        remove_pair_by_names(args.coma, args.comb)
        list_pairs()