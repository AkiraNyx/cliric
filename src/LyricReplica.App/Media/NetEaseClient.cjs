const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const packageRoot = path.join(process.argv[2], 'node_modules', '@neteasecloudmusicapienhanced', 'api');
const api = require(packageRoot);

async function prepareAudioProtocol() {
    const keyPath = path.join(os.tmpdir(), 'xeapi_public_key');
    let cached;
    try { cached = JSON.parse(fs.readFileSync(keyPath, 'utf8')); }
    catch (error) { if (error.code !== 'ENOENT' && !(error instanceof SyntaxError)) throw error; }
    if (cached?.sk && cached.deviceId) {
        global.deviceId = cached.deviceId;
        return;
    }

    const { generateDeviceId } = require(path.join(packageRoot, 'util'));
    const { getXeapiPublicKey } = require(path.join(packageRoot, 'util', 'xeapiKey'));
    const deviceId = generateDeviceId();
    const key = await getXeapiPublicKey({}, deviceId);
    fs.writeFileSync(keyPath, JSON.stringify(key), 'utf8');
    global.deviceId = deviceId;
}

async function run({ operation, cookie, key, keyword, id }) {
    switch (operation) {
        case 'qr': {
            const { body } = await api.login_qr_key({ timestamp: Date.now() });
            const key = body.data?.unikey;
            if (!key) throw new Error('QR key unavailable');
            const qr = await api.login_qr_create({ key, qrimg: true });
            return { key, image: qr.body.data.qrimg };
        }
        case 'poll': {
            const { body } = await api.login_qr_check({ key, timestamp: Date.now() });
            return { code: body.code, cookie: body.code === 803 ? body.cookie : undefined };
        }
        case 'search': {
            const { body } = await api.cloudsearch({ keywords: keyword, type: 1, limit: 30, cookie });
            return body;
        }
        case 'lyrics': {
            const { body } = await api.lyric_new({ id, cookie });
            return body;
        }
        case 'audio': {
            await prepareAudioProtocol();
            const { body } = await api.song_url_v1({ id, level: 'standard', cookie });
            const audio = body.data?.find(item => String(item.id) === String(id));
            return { code: audio?.code ?? body.code, url: audio?.url, preview: !!audio?.freeTrialInfo };
        }
        default: throw new Error('Unknown operation');
    }
}

const output = process.stdout.write.bind(process.stdout);
run(JSON.parse(fs.readFileSync(0, 'utf8')))
    .then(result => output('\nCLIRIC_RESULT ' + JSON.stringify(result) + '\n'))
    .catch(() => { process.exitCode = 1; });
