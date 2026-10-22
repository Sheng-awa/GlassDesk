"""Persistent stdio MCP client for verified P0 GUI work. No permission overrides."""
import json
import os
import queue
import subprocess
import sys
import threading
from datetime import datetime
from pathlib import Path

cua_driver = os.environ.get("GLASSDESK_CUA_DRIVER")
if not cua_driver:
    local_app_data = os.environ.get("LOCALAPPDATA")
    if not local_app_data:
        raise RuntimeError("Set GLASSDESK_CUA_DRIVER to the installed cua-driver.exe.")
    cua_driver = str(Path(local_app_data) / "Programs/Cua/cua-driver/bin/cua-driver.exe")
if not Path(cua_driver).is_file():
    raise RuntimeError("The configured cua-driver.exe does not exist.")

proc = subprocess.Popen(
    [cua_driver, "mcp"] + (['--socket', r'\\.\pipe\cua-driver'] if '--shared' in sys.argv else ['--direct']),
    stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    text=True, encoding="utf-8", creationflags=subprocess.CREATE_NO_WINDOW)
responses = queue.Queue()
errors = []

def read_stdout():
    for line in proc.stdout:
        try:
            responses.put(json.loads(line))
        except json.JSONDecodeError:
            errors.append(line[:200])

def read_stderr():
    for line in proc.stderr:
        errors.append(line[:500])

threading.Thread(target=read_stdout, daemon=True).start()
threading.Thread(target=read_stderr, daemon=True).start()
counter = 0
run_name = datetime.now().strftime('%Y%m%d-%H%M%S')

def rpc(method, params):
    global counter
    counter += 1
    proc.stdin.write(json.dumps(dict(jsonrpc="2.0", id=counter, method=method, params=params)) + "\n")
    proc.stdin.flush()
    while True:
        try:
            msg = responses.get(timeout=45)
        except queue.Empty:
            raise RuntimeError("MCP timeout: " + "".join(errors[-4:]))
        if msg.get("id") == counter:
            return msg

print(json.dumps(rpc("initialize", dict(protocolVersion="2024-11-05", capabilities={},
    clientInfo=dict(name="glassdesk-p0", version="0.1.0"))), ensure_ascii=False), flush=True)
proc.stdin.write(json.dumps(dict(jsonrpc="2.0", method="notifications/initialized", params={})) + "\n")
proc.stdin.flush()
try:
    for line in sys.stdin:
        request = json.loads(line)
        if request.get("name") == "client_exit":
            break
        if request.get('name') == 'client_tools':
            result = rpc('tools/list', {})
            selected = [t for t in result.get('result',{}).get('tools',[]) if t['name'] in request.get('tools',[])]
            if request.get('compact'):
                def compact_schema(value):
                    if isinstance(value, dict):
                        return {k: compact_schema(v) for k, v in value.items() if k not in ('description', 'title')}
                    if isinstance(value, list):
                        return [compact_schema(v) for v in value]
                    return value
                selected = [dict(name=t['name'], inputSchema=compact_schema(t['inputSchema'])) for t in selected]
            print(json.dumps(selected, ensure_ascii=False),flush=True)
            continue
        if request.get('name') == 'client_action':
            # Bounded observe -> guard -> act -> observe loop. All input still
            # goes through the driver's normal permission and delivery gates.
            target = request['target']
            state_rpc = rpc('tools/call',dict(name='get_window_state',arguments=target))
            payload = state_rpc.get('result',{})
            state = payload.get('structuredContent',{})
            if payload.get('isError'):
                print(json.dumps(state_rpc,ensure_ascii=False),flush=True)
                continue
            guard = request.get('guard',{})
            if ('title' in guard and state.get('window_title') != guard['title']) or ('bounds' in guard and state.get('window_bounds') != guard['bounds']):
                print(json.dumps(dict(refused='observation changed',observed=state),ensure_ascii=False),flush=True)
                continue
            action = request['action']
            arguments = dict(target,**action.get('args',{}))
            selector = action.get('selector')
            physical_element = None
            if selector:
                matches=[e for e in state.get('elements',[]) if e.get('label')==selector['label'] and e.get('role')==selector['role']]
                if len(matches)!=1:
                    print(json.dumps(dict(refused='selector not unique',matches=len(matches)),ensure_ascii=False),flush=True)
                    continue
                if request.get('physical'):
                    physical_element = matches[0]
                else:
                    arguments['element_token']=matches[0]['element_token']
            if request.get('physical'):
                frame = physical_element.get('frame') if physical_element else None
                if action['name'] != 'click' or not frame or not physical_element.get('enabled'):
                    print(json.dumps(dict(refused='physical click requires one enabled observed element with geometry'),ensure_ascii=False),flush=True)
                    continue
                desktop_args = {'session': target['session']} if 'session' in target else {}
                desktop_before = rpc('tools/call',dict(name='get_desktop_state',arguments=desktop_args))
                if desktop_before.get('result',{}).get('isError'):
                    print(json.dumps(desktop_before,ensure_ascii=False),flush=True)
                    continue
                arguments = dict(desktop_args,scope='desktop',delivery_mode=request.get('delivery_mode','background'),
                    x=frame['x']+frame['w']/2,y=frame['y']+frame['h']/2)
            action_rpc=rpc('tools/call',dict(name=action['name'],arguments=arguments))
            if request.get('physical'):
                desktop_after = rpc('tools/call',dict(name='get_desktop_state',arguments=desktop_args))
            after_arguments = request.get('after_args', dict(pid=target['pid']))
            after_arguments = dict(after_arguments)
            if 'session' in target:
                after_arguments.setdefault('session', target['session'])
            after_rpc=rpc('tools/call',dict(name=request.get('after_name','list_windows'),arguments=after_arguments))
            bundle=dict(before=state_rpc,action=action_rpc,after=after_rpc)
            if request.get('physical'):
                bundle.update(desktop_before=desktop_before,desktop_after=desktop_after)
            for item in bundle.values():
                for block in item.get('result',{}).get('content',[]):
                    if block.get('type')=='image':block.pop('data',None)
                sc=item.get('result',{}).get('structuredContent',{})
                sc.pop('tree_markdown',None);sc.pop('_note',None)
            archive=Path(__file__).resolve().parent.parent/'artifacts'/'p0'/'cua'/run_name
            archive.mkdir(parents=True,exist_ok=True)
            (archive/f'loop-{counter:04}.json').write_text(json.dumps(bundle,ensure_ascii=False,indent=2),encoding='utf-8')
            brief_before={k:v for k,v in state.items() if k not in ['elements','tree_markdown','_note','_legacy_windows']}
            print(json.dumps(dict(before=brief_before,action=action_rpc.get('result'),after=after_rpc.get('result')),ensure_ascii=False),flush=True)
            continue
        result = rpc("tools/call", dict(name=request["name"], arguments=request.get("args", {})))
        # Keep binary image data out of text. Observations use screenshot_out_file.
        for block in result.get("result", {}).get("content", []):
            if block.get("type") == "image":
                block.pop("data", None)
        archive = Path(__file__).resolve().parent.parent / 'artifacts' / 'p0' / 'cua' / run_name
        archive.mkdir(parents=True, exist_ok=True)
        (archive / f'rpc-{counter:04}.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
        payload = result.get('result', result)
        state = payload.get('structuredContent')
        if state is not None:
            # Keep every exact token, but omit duplicate Markdown and long notes.
            brief = {k:v for k,v in state.items() if k not in ['_note', 'tree_markdown', '_legacy_windows']}
            print(json.dumps(dict(id=counter, result=brief, isError=payload.get('isError',False), diagnostics=payload.get('content') if payload.get('isError') else None),ensure_ascii=False),flush=True)
        else:
            print(json.dumps(result,ensure_ascii=False),flush=True)
except Exception as ex:
    print(json.dumps(dict(error=str(ex)), ensure_ascii=False), flush=True)
finally:
    proc.stdin.close()
    try:
        proc.wait(timeout=5)
    except subprocess.TimeoutExpired:
        proc.terminate()  # This exact helper process owns no user application.
