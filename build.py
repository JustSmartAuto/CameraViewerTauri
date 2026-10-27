#!/usr/bin/env python
# -*- coding: utf-8 -*-
"""将 viewer_template.html + three.js 打包为单文件 Cognex3DRawFileViewer.html"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))

def read(p):
    with open(p, encoding='utf-8') as f:
        return f.read()

three = read(os.path.join(HERE, '.build', 'three.min.js'))
orbit = read(os.path.join(HERE, '.build', 'OrbitControls.js'))
tpl = read(os.path.join(HERE, 'viewer_template.html'))

# 防止库内容中的 </script> 提前闭合标签
three = three.replace('</script', '<\\/script')
orbit = orbit.replace('</script', '<\\/script')

out = tpl.replace('/*__THREE__*/', three, 1).replace('/*__ORBIT__*/', orbit, 1)
dst = os.path.join(HERE, 'Cognex3DRawFileViewer.html')
with open(dst, 'w', encoding='utf-8') as f:
    f.write(out)
print('built:', dst, os.path.getsize(dst), 'bytes')
