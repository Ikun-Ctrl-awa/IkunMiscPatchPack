"""检查源码提交树；只用 Python 标准库，不读取游戏或账户目录。"""
from pathlib import Path
import re,sys
r=Path(__file__).resolve().parents[1]
allowed={'.cs','.csproj','.py','.md','.json','.props','.example','.ps1'}
special={'.gitignore','.gitattributes','.editorconfig'}
excluded={'.git','bin','obj','__pycache__'}
errors=[];count=0
patterns=[r'(?i)[A-Z]:[/\\]Users[/\\](?!Public(?:[/\\]|$))[^/\\\s"<>]+',r'7656119[0-9]{10}',r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',r'(?i)gh[pousr]_[A-Za-z0-9]{25,}',r'github_pat_[A-Za-z0-9_]{20,}',r'AKIA[0-9A-Z]{16}']
for p in r.rglob('*'):
 if not p.is_file() or set(p.relative_to(r).parts)&excluded:continue
 rel=p.relative_to(r).as_posix()
 if p.name=='Local.Build.props':continue
 if p.name not in special and p.suffix not in allowed:errors.append(rel+': unexpected file type');continue
 if p.stat().st_size>1024*1024:errors.append(rel+': oversized source');continue
 try:text=p.read_text(encoding='utf-8-sig')
 except UnicodeDecodeError:errors.append(rel+': non UTF-8 content');continue
 if '\0' in text:errors.append(rel+': binary content')
 for pattern in patterns:
  if re.search(pattern,text):errors.append(rel+': personal path or credential pattern')
 count+=1
print('SOURCE_CHECK files='+str(count)+' failed='+str(len(errors)))
for error in errors:print(error)
sys.exit(bool(errors))
