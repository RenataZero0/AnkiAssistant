// 校验 editor.html 里的内联 JS 是否有语法错误（真机上只会看到 "xxx is not defined"）
const fs = require('fs');
const path = process.argv[2];
const html = fs.readFileSync(path, 'utf8');
const re = /<script(?![^>]*\bsrc=)[^>]*>([\s\S]*?)<\/script>/gi;
let m, i = 0, bad = 0;
while ((m = re.exec(html)) !== null) {
  i++;
  const code = m[1];
  try {
    new Function(code);
    console.log('script #' + i + ' (' + code.length + ' chars): OK');
  } catch (e) {
    bad++;
    console.log('script #' + i + ' (' + code.length + ' chars): SYNTAX ERROR -> ' + e.message);
  }
}
console.log('inline scripts: ' + i + ', errors: ' + bad);
process.exit(bad ? 1 : 0);
