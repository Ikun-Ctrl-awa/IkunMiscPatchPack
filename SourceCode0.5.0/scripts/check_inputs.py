from pathlib import Path
import argparse,json,hashlib,sys
parser=argparse.ArgumentParser();parser.add_argument('--root',required=True);args=parser.parse_args()
items=json.loads((Path(__file__).resolve().parents[1]/'docs/required-inputs.json').read_text(encoding='utf-8'))['Files'];bad=[]
for item in items:
 p=Path(args.root)/item['Path']
 if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest().upper()!=item['Sha256']:bad.append(item['Path'])
print('INPUT_CHECK files='+str(len(items))+' failed='+str(len(bad)))
for path in bad:print('Missing or wrong input: '+path)
sys.exit(bool(bad))
