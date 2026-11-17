const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const packageRoot = path.join(process.argv[2], 'node_modules', '@neteasecloudmusicapienhanced', 'api');
const api = require(packageRoot);
const qualityLevels = ['standard', 'higher', 'exhigh', 'lossless', 'hires', 'jyeffect', 'sky', 'jymaster', 'vivid'];

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

async function tracksWithRights(songs, cookie) {
    if (!Array.isArray(songs)) throw new Error('Track list unavailable');
    const ids = songs.map(song => song.id).filter(id => Number.isSafeInteger(id) && id > 0);
    const rights = Object.create(null);
    if (ids.length) {
        try {
            await prepareAudioProtocol();
            const { body } = await api.song_url_v1({ id: ids.join(','), level: 'standard', cookie });
            if (body.code === 200 && Array.isArray(body.data)) {
                for (const item of body.data) {
                    if (item.freeTrialInfo) rights[item.id] = 'preview';
                    else if (item.code === 200 && item.url) rights[item.id] = 'full';
                    else if (item.code === 404 || item.code === 403 || item.code === 200) rights[item.id] = 'unavailable';
                }
            }
        } catch { /* Track information is still usable if the entitlement request fails. */ }
    }
    return { songs, rights };
}

async function libraryPage(ids, offset, cookie) {
    if (!Array.isArray(ids)) throw new Error('Playlist unavailable');
    const page = ids.slice(offset, offset + 30);
    if (!page.length) return { songs: [], rights: {}, nextOffset: null };
    const { body } = await api.song_detail({ ids: page.join(','), cookie });
    if (!Array.isArray(body.songs)) throw new Error('Playlist tracks unavailable');
    const byId = new Map(body.songs.map(song => [song.id, song]));
    const result = await tracksWithRights(page.map(id => byId.get(id)).filter(Boolean), cookie);
    return { ...result, nextOffset: offset + page.length < ids.length ? offset + page.length : null };
}

async function run({ operation, cookie, key, keyword, id, offset = 0, level }) {
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
            if (!body.result) throw new Error('Search result unavailable');
            return tracksWithRights(body.result.songs ?? [], cookie);
        }
        case 'account': {
            const { body } = await api.user_account({ cookie });
            if (body.code !== 200) throw new Error('Account status unavailable');
            return { signedIn: !!body.profile?.userId, name: body.profile?.nickname, avatarUrl: body.profile?.avatarUrl };
        }
        case 'daily': {
            const { body } = await api.recommend_songs({ cookie });
            return tracksWithRights(body.data?.dailySongs, cookie);
        }
        case 'recommendations': {
            const { body } = await api.recommend_resource({ cookie });
            if (!Array.isArray(body.recommend)) throw new Error('Recommended playlists unavailable');
            return { playlists: body.recommend.map(({ id, name, picUrl, coverImgUrl }) => ({ id, name, coverUrl: picUrl || coverImgUrl })) };
        }
        case 'playlist': {
            const { body } = await api.playlist_detail({ id, cookie });
            return libraryPage(body.playlist?.trackIds?.map(track => track.id), offset, cookie);
        }
        case 'liked': {
            const { body: account } = await api.user_account({ cookie });
            const uid = account.profile?.userId;
            if (!uid) throw new Error('Login required');
            const { body } = await api.user_playlist({ uid, limit: 50, cookie });
            const liked = body.playlist?.find(item => item.specialType === 5 && String(item.userId ?? item.creator?.userId) === String(uid));
            if (!liked) throw new Error('Liked playlist unavailable');
            const { body: details } = await api.playlist_detail({ id: liked.id, cookie });
            return libraryPage(details.playlist?.trackIds?.map(track => track.id), offset, cookie);
        }
        case 'lyrics': {
            const { body } = await api.lyric_new({ id, cookie });
            return body;
        }
        case 'quality': {
            await prepareAudioProtocol();
            const allowed = new Set();
            for (let i = 0; i < qualityLevels.length; i += 3) {
                const replies = await Promise.allSettled(qualityLevels.slice(i, i + 3).map(level => api.song_url_v1({ id, level, cookie })));
                for (const reply of replies) {
                    if (reply.status !== 'fulfilled') continue;
                    const audio = reply.value.body.data?.find(item => String(item.id) === String(id));
                    if (audio?.code === 200 && audio.url && !audio.freeTrialInfo && qualityLevels.includes(audio.level))
                        allowed.add(audio.level);
                }
            }
            return { levels: qualityLevels.filter(level => allowed.has(level)) };
        }
        case 'audio': {
            await prepareAudioProtocol();
            const { body } = await api.song_url_v1({ id, level, cookie });
            const audio = body.data?.find(item => String(item.id) === String(id));
            return { code: audio?.code ?? body.code, url: audio?.url, preview: !!audio?.freeTrialInfo, level: audio?.level };
        }
        default: throw new Error('Unknown operation');
    }
}

const output = process.stdout.write.bind(process.stdout);
run(JSON.parse(fs.readFileSync(0, 'utf8')))
    .then(result => output('\nCLIRIC_RESULT ' + JSON.stringify(result) + '\n'))
    .catch(() => { process.exitCode = 1; });
