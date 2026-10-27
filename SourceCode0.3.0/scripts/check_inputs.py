"""只读核对本地私有构建资源；不生成、下载或上传任何游戏文件。"""
from pathlib import Path
import argparse
import hashlib
import json
import os

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--root',default=os.environ.get('STS2_PATCH_INPUTS_DIR'))
args=parser.parse_args()
if not args.root:
    parser.error('请用--root或STS2_PATCH_INPUTS_DIR指定私有构建输入目录。')
root=Path(args.root).resolve()
manifest=json.loads((Path(__file__).resolve().parents[1]/'docs/required-inputs.json').read_text(encoding='utf-8'))
failed=[]
for item in manifest['BuildInputs']:
    path=(root/item['Path']).resolve()
    if not path.is_relative_to(root):
        failed.append(item['Path']+': invalid path')
    elif not path.is_file():
        failed.append(item['Path']+': missing')
    elif path.stat().st_size!=item['Bytes'] or hashlib.sha256(path.read_bytes()).hexdigest().upper()!=item['Sha256']:
        failed.append(item['Path']+': hash mismatch')
print('INPUT_CHECK inputs='+str(len(manifest['BuildInputs']))+' failed='+str(len(failed)))
for error in failed:
    print(error)
raise SystemExit(bool(failed))
