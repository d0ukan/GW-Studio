"""Linux-only serial integration fixture. Usage: python3 serial_pty_test.py /path/to/dotnet.
Uses the production SerialDeviceService through a real OS pseudo-terminal, not a serial mock.
"""
import os, pty, select, subprocess, sys, threading, time
from pathlib import Path
master, slave = pty.openpty()
port = os.ttyname(slave)
stop = threading.Event()
errors = []
def serve():
    buffer = b''
    while not stop.is_set():
        if not select.select([master], [], [], .1)[0]: continue
        try: data = os.read(master, 4096)
        except OSError: continue
        buffer += data
        while b'\n' in buffer:
            line, buffer = buffer.split(b'\n', 1)
            if b'\r' in line: errors.append('Unexpected CR in command: '+repr(line))
            command = line.decode('ascii')
            if command == 'UNPLUG?': os.close(master); return
            if command == 'SLOW?': time.sleep(.3); reply = 'LATE'
            elif command == '*IDN?': reply = 'GW Instek,GPP-4323,SN:PTY,V1.07'
            elif command == 'VOUT1?': reply = '4.996V'
            elif command == 'IOUT1?': reply = '0.1250A'
            elif command == 'VSET1:5.000': continue
            else: errors.append('Unexpected command: '+command); continue
            try: os.write(master, (reply+'\r\n').encode('ascii'))
            except OSError: pass
thread = threading.Thread(target=serve, daemon=True); thread.start()
try:
    dll = Path(__file__).parent / 'bin/Release/net8.0/GW-Studio.Tests.dll'
    result = subprocess.run([sys.argv[1], str(dll), port], timeout=30)
    if errors: print(errors); sys.exit(1)
    sys.exit(result.returncode)
finally:
    stop.set(); thread.join(timeout=1)
    try: os.close(master)
    except OSError: pass
    os.close(slave)
