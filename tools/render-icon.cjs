// Optional artwork regeneration: npm install --no-save sharp
// Normal .NET builds use the committed ICO and do not require Node.js.
const fs = require('node:fs/promises');
const path = require('node:path');
const sharp = require('sharp');

async function main() {
  const assets = path.resolve(__dirname, '../assets');
  const svg = await fs.readFile(path.join(assets, 'ApoSwitch.svg'));
  const render = size => sharp(Buffer.from(svg.toString('utf8').replace(
    /(<svg\b[^>]*\bwidth=")[^"]+("\s+height=")[^"]+(")/,
    (_, before, middle, after) => `${before}${size}${middle}${size}${after}`
  ))).ensureAlpha().png({ compressionLevel: 9 });
  const sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];
  const frames = [];
  for (const size of sizes) {
    // Render every size from the vector master, not from an intermediate bitmap.
    frames.push(await render(size).toBuffer());
  }
  const header = Buffer.alloc(6 + sizes.length * 16);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(sizes.length, 4);
  let offset = header.length;
  sizes.forEach((size, i) => {
    const entry = 6 + i * 16;
    header[entry] = header[entry + 1] = size === 256 ? 0 : size;
    header.writeUInt16LE(1, entry + 4);
    header.writeUInt16LE(32, entry + 6);
    header.writeUInt32LE(frames[i].length, entry + 8);
    header.writeUInt32LE(offset, entry + 12);
    offset += frames[i].length;
  });
  await fs.writeFile(path.join(assets, 'ApoSwitch.ico'), Buffer.concat([header, ...frames]));
  for (const size of [512, 1024]) {
    await render(size).toFile(path.join(assets, `ApoSwitch-${size}.png`));
  }
  console.log(`Generated lossless ICO frames: ${sizes.join(', ')}; PNG: 512, 1024.`);
}
main().catch(error => { console.error(error); process.exitCode = 1; });
