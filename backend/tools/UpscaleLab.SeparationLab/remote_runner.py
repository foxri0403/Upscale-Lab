"""Run an isolated lab through an SSH loopback tunnel; never change production services."""
import argparse,getpass,hashlib,json,os,pathlib,pwd,re,shlex,shutil,signal,subprocess,tarfile,time,urllib.request
p=argparse.ArgumentParser();p.add_argument('mode',choices=['stage','run','stop']);p.add_argument('run_id')
p.add_argument('--settings',required=True);p.add_argument('--port',type=int,default=5091);p.add_argument('--sha256');args=p.parse_args()
assert re.fullmatch(r'separation-lab-\d{8}-\d{6}-[a-f0-9]{8}',args.run_id)
assert 1024<=args.port<=65535
owner=os.environ.get('SUDO_USER') or getpass.getuser();home=pathlib.Path(pwd.getpwnam(owner).pw_dir)
base=home/'upscale-separation-labs';slot=base/args.run_id;archive=base/(args.run_id+'.tar.gz')
assert slot.parent==base and not slot.is_symlink()
def environment():
    # Provider credentials stay on this host. Production DB/JWT/payment settings are excluded.
    env={k:v for k,v in os.environ.items() if k in ['PATH','HOME','LANG','AWS_PROFILE','AWS_REGION','AWS_DEFAULT_REGION','AWS_ACCESS_KEY_ID','AWS_SECRET_ACCESS_KEY','AWS_SESSION_TOKEN']}
    for line in pathlib.Path(args.settings).read_text().splitlines():
        if not line.strip() or line.lstrip().startswith('#') or '=' not in line:continue
        k,v=line.split('=',1);k=k.strip()
        if k.startswith(('AWS__','RunPod__')):env[k]=shlex.split(v)[0] if v.startswith(('"',"'")) else v.strip()
    env.update(ASPNETCORE_ENVIRONMENT='Development',ASPNETCORE_URLS=f'http://127.0.0.1:{args.port}',UPSCALE_LAB_JOURNAL=str(slot/'journal.json'),DOTNET_ROOT=str(home/'.dotnet8'))
    return env
dotnet=home/'.dotnet8/dotnet';dll=slot/'UpscaleLab.SeparationLab.dll'
if args.mode=='stage':
    assert not slot.exists() and args.sha256 and hashlib.sha256(archive.read_bytes()).hexdigest()==args.sha256
    slot.mkdir(mode=0o700)
    with tarfile.open(archive) as tar:
        for member in tar.getmembers():
            target=(slot/member.name).resolve();assert (target==slot or slot in target.parents) and not member.issym() and not member.islnk()
        tar.extractall(slot,filter='data')
    assert dll.is_file();(slot/'run-id').write_text(args.run_id)
    print(json.dumps({'staged':args.run_id}),flush=True)
elif args.mode=='run':
    assert (slot/'run-id').read_text()==args.run_id and dotnet.is_file()
    (slot/'pid').write_text(str(os.getpid()));os.chdir(slot)
    os.execve(dotnet,[str(dotnet),str(dll)],environment())
else:
    assert (slot/'run-id').read_text()==args.run_id
    pid=int((slot/'pid').read_text());proc=pathlib.Path('/proc')/str(pid)
    if proc.exists():
        cmd=(proc/'cmdline').read_bytes().split(b'\0');assert str(dll).encode() in cmd,'PID no longer belongs to this lab'
        with urllib.request.urlopen(f'http://127.0.0.1:{args.port}/health',timeout=5) as response:health=json.load(response)
        assert health.get('isolated') and health['activeJobs']==0,'Wait until the current GPU job finishes before stopping'
        os.kill(pid,signal.SIGINT)
        for _ in range(100):
            if not proc.exists():break
            time.sleep(.1)
        if proc.exists():raise RuntimeError('Lab process is still stopping; retry stop shortly')
    subprocess.run([str(dotnet),str(dll),'--cleanup'],cwd=slot,env=environment(),check=True)
    assert slot.resolve().parent==base.resolve() and slot.name==args.run_id
    shutil.rmtree(slot);archive.unlink(missing_ok=True)
    print(json.dumps({'stopped':args.run_id,'ownTestObjectsRemoved':True}),flush=True)
