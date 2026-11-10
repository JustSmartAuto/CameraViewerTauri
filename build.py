#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""将 viewer_template.html + three.js 打包为单文件 Cognex3DRawFileViewer.html"""
import base64
import gzip
import os

HERE = os.path.dirname(os.path.abspath(__file__))

def read(p):
    with open(p, encoding='utf-8') as f:
        return f.read()

def read_gzip(p):
    with open(p, 'rb') as f:
        return gzip.decompress(f.read())

three = read(os.path.join(HERE, '.build', 'three.min.js'))
orbit = read(os.path.join(HERE, '.build', 'OrbitControls.js'))
tpl = read(os.path.join(HERE, 'viewer_template.html'))
framework = read_gzip(os.path.join(HERE, 'webhmi', 'Framework.js.gz')).decode('utf-8')
tako_start = framework.index('var Tako=function()')
tako_end_marker = '"object"==typeof exports&&(exports.Tako=Tako)'
tako_end = framework.index(tako_end_marker, tako_start) + len(tako_end_marker)
tako = framework[tako_start:tako_end]
wasm = read_gzip(os.path.join(HERE, 'webhmi', 'takoweb.wasm.gz'))
wasm_uri = 'data:application/octet-stream;base64,' + base64.b64encode(wasm).decode('ascii')
tako = tako.replace('wasmBinaryFile="takoweb.wasm"',
                    'wasmBinaryFile="' + wasm_uri + '"', 1)

# 防止库内容中的 </script> 提前闭合标签
three = three.replace('</script', '<\\/script')
orbit = orbit.replace('</script', '<\\/script')
tako = tako.replace('</script', '<\\/script')

out = (tpl.replace('/*__THREE__*/', three, 1)
       .replace('/*__ORBIT__*/', orbit, 1)
       .replace('/*__TAKO__*/', tako, 1))
dst = os.path.join(HERE, 'Cognex3DRawFileViewer.html')
with open(dst, 'w', encoding='utf-8') as f:
    f.write(out)
print('built:', dst, os.path.getsize(dst), 'bytes')
