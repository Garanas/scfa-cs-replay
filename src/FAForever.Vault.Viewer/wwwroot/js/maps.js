// Images of a map for the map page, drawn from the data the app parsed: the terrain from the
// heightmap and colour grids (regions, owners, density) from one value per cell. Each comes back as
// a blob URL that the page's SVG uses as an <image>, at one pixel per ogrid; the page releases the
// URLs when the map changes. Per pixel work is far faster here than in the WebAssembly interpreter.
window.fafMaps = {
    // a ramp from low and green to high and pale, darkened and lightened by the hillshade
    ramp: [[0, [86, 104, 72]], [0.35, [138, 132, 96]], [0.65, [170, 150, 120]], [1, [222, 220, 214]]],

    rampAt: function (t) {
        const ramp = window.fafMaps.ramp;
        for (let i = 1; i < ramp.length; i++) {
            if (t <= ramp[i][0]) {
                const [t0, c0] = ramp[i - 1], [t1, c1] = ramp[i];
                const f = (t - t0) / (t1 - t0);
                return [0, 1, 2].map(j => c0[j] + (c1[j] - c0[j]) * f);
            }
        }
        return ramp[ramp.length - 1][1];
    },

    toUrl: function (canvas) {
        return new Promise(resolve => canvas.toBlob(blob => resolve(URL.createObjectURL(blob)), 'image/png'));
    },

    // heightBytes: the heightmap's samples, (width + 1) x (height + 1) unsigned 16 bit, little endian.
    // mode: relief, elevation, cliffs or muted (a grey hillshade to draw other layers on).
    terrain: function (heightBytes, width, height, scale, water, mode) {
        const samples = new Uint16Array(heightBytes.buffer, heightBytes.byteOffset, heightBytes.byteLength / 2);
        const stride = width + 1;
        const H = (x, z) => samples[z * stride + x] * scale;
        let min = Infinity, max = -Infinity;
        for (let i = 0; i < samples.length; i++) {
            const v = samples[i] * scale;
            if (v < min) min = v;
            if (v > max) max = v;
        }
        const range = Math.max(1e-6, max - min);
        const surface = water === null || water === undefined ? -Infinity : water;
        const light = [-0.55, 0.7, -0.45];
        const length = Math.hypot(light[0], light[1], light[2]);

        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext('2d');
        const image = context.createImageData(width, height);
        const data = image.data;
        for (let z = 0; z < height; z++) {
            for (let x = 0; x < width; x++) {
                const t = H(x, z), right = H(x + 1, z), down = H(x, z + 1), corner = H(x + 1, z + 1);
                const dx = right - t, dz = down - t;
                const nx = -dx * 1.6, ny = 1, nz = -dz * 1.6;
                const shade = Math.max(0, (nx * light[0] + ny * light[1] + nz * light[2]) / (Math.hypot(nx, ny, nz) * length));
                let rgb;
                if (mode === 'cliffs') {
                    const steep = Math.max(Math.abs(dx), Math.abs(dz), Math.abs(corner - down), Math.abs(corner - right)) >= 0.75;
                    const g = 60 + shade * 150;
                    rgb = steep ? [214, 74, 64] : (t < surface ? [g * 0.55, g * 0.7, g * 0.95] : [g, g, g + 6]);
                } else if (mode === 'muted') {
                    const g = 40 + shade * 120;
                    rgb = t < surface ? [g * 0.55, g * 0.7, g * 0.95] : [g, g, g + 8];
                } else if (mode === 'elevation') {
                    const v = (t - min) / range;
                    rgb = t < surface ? [20, 50, 110] : [30 + v * 220, 40 + v * 190, 90 + v * 120];
                } else {
                    if (t < surface) {
                        const depth = Math.min(1, (surface - t) / 25);
                        const k = 0.75 + shade * 0.35;
                        rgb = [(40 - depth * 20) * k, (92 - depth * 40) * k, (128 - depth * 30) * k];
                    } else {
                        const base = window.fafMaps.rampAt((t - min) / range);
                        const k = 0.45 + shade * 0.75;
                        rgb = [base[0] * k, base[1] * k, base[2] * k];
                    }
                }
                const p = (z * width + x) * 4;
                data[p] = rgb[0];
                data[p + 1] = rgb[1];
                data[p + 2] = rgb[2];
                data[p + 3] = 255;
            }
        }
        context.putImageData(image, 0, 0);
        return window.fafMaps.toUrl(canvas);
    },

    // The preview at the start of the .scmap: a DDS file the game lobby shows, 256 x 256 pixels in
    // every map of the vault, uncompressed with 32 bits per pixel (the bit masks say where red, green
    // and blue are). Null for another kind of DDS (compressed), so the page falls back to the relief.
    preview: function (dds) {
        const view = new DataView(dds.buffer, dds.byteOffset, dds.byteLength);
        if (dds.byteLength < 128 || view.getUint32(0, true) !== 0x20534444) {
            return null;
        }
        const height = view.getUint32(12, true), width = view.getUint32(16, true);
        const flags = view.getUint32(80, true), bits = view.getUint32(88, true);
        const masks = [view.getUint32(92, true), view.getUint32(96, true), view.getUint32(100, true)];
        if (!(flags & 0x40) || bits !== 32 || width === 0 || height === 0 || dds.byteLength < 128 + width * height * 4) {
            return null;
        }
        const shifts = masks.map(mask => mask === 0 ? 0 : Math.log2(mask & -mask));

        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext('2d');
        const image = context.createImageData(width, height);
        const data = image.data;
        for (let i = 0; i < width * height; i++) {
            const pixel = view.getUint32(128 + i * 4, true);
            const p = i * 4;
            data[p] = (pixel & masks[0]) >>> shifts[0];
            data[p + 1] = (pixel & masks[1]) >>> shifts[1];
            data[p + 2] = (pixel & masks[2]) >>> shifts[2];
            data[p + 3] = 255;
        }
        context.putImageData(image, 0, 0);
        return window.fafMaps.toUrl(canvas);
    },

    // Each cell in the colour of the start position closest to it in a straight line. starts: x, z
    // of each start position; palette: four bytes (r, g, b, a) per start position.
    closest: function (width, height, starts, palette) {
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext('2d');
        const image = context.createImageData(width, height);
        const data = image.data;
        const count = starts.length / 2;
        for (let z = 0; z < height; z++) {
            for (let x = 0; x < width; x++) {
                let best = 0, bestDistance = Infinity;
                for (let i = 0; i < count; i++) {
                    const dx = x + 0.5 - starts[i * 2], dz = z + 0.5 - starts[i * 2 + 1];
                    const distance = dx * dx + dz * dz;
                    if (distance < bestDistance) {
                        bestDistance = distance;
                        best = i;
                    }
                }
                const c = (best % (palette.length / 4)) * 4;
                const p = (z * width + x) * 4;
                data[p] = palette[c];
                data[p + 1] = palette[c + 1];
                data[p + 2] = palette[c + 2];
                data[p + 3] = palette[c + 3];
            }
        }
        context.putImageData(image, 0, 0);
        return window.fafMaps.toUrl(canvas);
    },

    // values: one byte per cell, row by row; palette: four bytes (r, g, b, a) per value.
    grid: function (values, width, height, palette) {
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const context = canvas.getContext('2d');
        const image = context.createImageData(width, height);
        const data = image.data;
        for (let i = 0; i < values.length; i++) {
            const c = values[i] * 4;
            const p = i * 4;
            data[p] = palette[c];
            data[p + 1] = palette[c + 1];
            data[p + 2] = palette[c + 2];
            data[p + 3] = palette[c + 3];
        }
        context.putImageData(image, 0, 0);
        return window.fafMaps.toUrl(canvas);
    },

    release: function (urls) {
        for (const url of urls) {
            URL.revokeObjectURL(url);
        }
    },
};
