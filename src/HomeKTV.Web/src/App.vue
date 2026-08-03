<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as signalR from '@microsoft/signalr'
import { api, type Playback, type PlaybackControlCommand, type QueueItem, type Session, type Song } from './api'

type Tab='songs'|'queue'|'mine'
function loadNickname(){try{return window.localStorage.getItem('homektv-nickname')??''}catch{return''}}
function saveNickname(value:string){try{window.localStorage.setItem('homektv-nickname',value)}catch{/* Safari 隐私模式仍允许本次扫码会话继续使用。 */}}
const session=ref<Session|null>(null), nickname=ref(loadNickname())
const query=ref(''), language=ref(''), songs=ref<Song[]>([]), queue=ref<QueueItem[]>([])
const playback=ref<Playback>({queueItemId:null,title:null,artist:null,state:'Idle',nextTitle:null,lyricsVisible:true,lyricsAvailable:false,audioMode:'Original',canUseAccompaniment:false,volume:80}), volumeDraft=ref(80), tab=ref<Tab>('songs')
const showControls=ref(false)
const busy=ref(false), controlBusy=ref(false), message=ref(''), online=ref(navigator.onLine)
let connection:signalR.HubConnection|null=null, searchTimer=0, reconnectTimer=0, volumeTimer=0
const myQueue=computed(()=>queue.value.filter(x=>x.isMine))

async function enter(){if(!nickname.value.trim())return;busy.value=true;try{session.value=await api.createSession(nickname.value.trim());saveNickname(nickname.value.trim());await load();await connect()}catch(e){notice(e)}finally{busy.value=false}}
async function load(){await loadState();await search()}
async function loadState(){if(!session.value)return;const state=await api.state(session.value);queue.value=state.queue;playback.value=state.playback}
async function search(){if(!session.value)return;busy.value=true;try{songs.value=await api.search(query.value,language.value,session.value)}catch(e){notice(e)}finally{busy.value=false}}
async function order(song:Song){if(!session.value)return;try{await api.enqueue(song.id,session.value);message.value='已点《'+song.title+'》';setTimeout(()=>message.value='',1800)}catch(e){notice(e)}}
async function remove(item:QueueItem){if(!session.value||!confirm('删除《'+item.song.title+'》？'))return;try{await api.remove(item.id,session.value);queue.value=queue.value.filter(x=>x.id!==item.id)}catch(e){notice(e)}}
async function move(item:QueueItem,direction:-1|1){if(!session.value)return;try{await api.move(item.id,direction,session.value);await loadState()}catch(e){notice(e)}}
async function favorite(song:Song){if(!session.value)return;try{song.isFavorite=!song.isFavorite;await api.favorite(song.id,song.isFavorite,session.value)}catch(e){song.isFavorite=!song.isFavorite;notice(e)}}
async function toggleLyrics(){if(!session.value||controlBusy.value)return;controlBusy.value=true;try{const visible=!playback.value.lyricsVisible;await api.setLyricsVisible(visible,session.value);playback.value={...playback.value,lyricsVisible:visible};message.value=visible?'歌词已显示':'歌词已隐藏';setTimeout(()=>message.value='',1800)}catch(e){notice(e)}finally{controlBusy.value=false}}
async function controlPlayback(command:PlaybackControlCommand){if(!session.value||controlBusy.value)return;const wasPaused=playback.value.state==='Paused';controlBusy.value=true;try{await api.playbackControl(command,session.value);await loadState();message.value={togglePause:wasPaused?'已继续':'已暂停',restart:'已重唱',skip:'已切歌',original:'已切换原唱',accompaniment:'已切换伴唱'}[command];setTimeout(()=>message.value='',1800)}catch(e){notice(e)}finally{controlBusy.value=false}}
function queueVolume(event:Event){const value=Math.max(0,Math.min(125,Number((event.target as HTMLInputElement).value)));volumeDraft.value=value;clearTimeout(volumeTimer);volumeTimer=window.setTimeout(()=>void setVolume(value),120)}
async function setVolume(value:number){volumeTimer=0;if(!session.value)return;try{await api.setVolume(value,session.value);playback.value={...playback.value,volume:value}}catch(e){notice(e);await loadState()}}
function notice(error:unknown){message.value=error instanceof Error?error.message:'操作失败，请稍后重试';setTimeout(()=>message.value='',2800)}
function scheduleReconnect(){clearTimeout(reconnectTimer);reconnectTimer=window.setTimeout(()=>void connect(),3000)}
async function connect(){if(!connection){connection=new signalR.HubConnectionBuilder().withUrl('/hub').withAutomaticReconnect([0,1000,3000,5000]).build();connection.on('queueChanged',()=>void loadState());connection.on('playbackChanged',(state:Playback)=>playback.value=state);connection.onreconnecting(()=>online.value=false);connection.onreconnected(()=>{online.value=true;void load()});connection.onclose(()=>{online.value=false;scheduleReconnect()})}if(connection.state!==signalR.HubConnectionState.Disconnected)return;try{await connection.start();online.value=true;clearTimeout(reconnectTimer)}catch{online.value=false;scheduleReconnect()}}
watch([query,language],()=>{clearTimeout(searchTimer);searchTimer=window.setTimeout(()=>void search(),250)})
watch(()=>playback.value.volume,value=>{if(!volumeTimer)volumeDraft.value=value},{immediate:true})
const onOnline=()=>{online.value=true;void connect()}, onOffline=()=>online.value=false
onMounted(()=>{window.addEventListener('online',onOnline);window.addEventListener('offline',onOffline)})
onBeforeUnmount(()=>{clearTimeout(reconnectTimer);clearTimeout(volumeTimer);window.removeEventListener('online',onOnline);window.removeEventListener('offline',onOffline);void connection?.stop()})
</script>

<template>
  <main v-if="!session" class="welcome">
    <div class="brand-mark">H</div><p class="eyebrow">HOME PARTY · 随手点歌</p><h1>今晚，想唱什么？</h1>
    <p class="muted">连接家庭 Wi-Fi 后输入昵称，就可以一起排歌。</p>
    <form class="join" @submit.prevent="enter"><label>你的昵称</label><input v-model="nickname" maxlength="20" autofocus placeholder="例如：小夏" /><button :disabled="busy||!nickname.trim()">{{busy?'正在加入…':'进入点歌台'}}</button></form>
  </main>
  <main v-else class="shell">
    <header><div><p class="eyebrow">HOMEKTV</p><h1>嗨，{{session.nickname}}</h1></div><span class="status" :class="{off:!online}"><i></i>{{online?'已连接':'重连中'}}</span></header>
    <section class="now"><div class="cover"><span>♫</span></div><div class="now-copy"><p>正在播放</p><h2>{{playback.title||'等待第一首歌'}}</h2><span>{{playback.artist||'点一首喜欢的歌吧'}} · {{playback.lyricsAvailable?(playback.lyricsVisible?'歌词显示':'歌词隐藏'):'无歌词'}}</span></div><button class="control-trigger" :class="{open:showControls}" :aria-expanded="showControls" @click="showControls=!showControls"><span>控制</span><small>{{showControls?'收起':'展开'}}</small></button></section>
    <section v-if="showControls" class="admin-control"><div class="admin-buttons"><button :disabled="controlBusy||playback.state==='Idle'" @click="controlPlayback('togglePause')">{{playback.state==='Paused'?'继续':'暂停'}}</button><button :disabled="controlBusy||playback.state==='Idle'" @click="controlPlayback('restart')">重唱</button><button :disabled="controlBusy||playback.state==='Idle'" @click="controlPlayback('skip')">切歌</button><button :class="{selected:playback.audioMode==='Original'}" :aria-pressed="playback.audioMode==='Original'" :disabled="controlBusy||playback.state==='Idle'" @click="controlPlayback('original')">原唱</button><button :class="{selected:playback.audioMode==='Accompaniment'}" :aria-pressed="playback.audioMode==='Accompaniment'" :disabled="controlBusy||playback.state==='Idle'||!playback.canUseAccompaniment" @click="controlPlayback('accompaniment')">伴唱</button><button :disabled="controlBusy||playback.state==='Idle'||!playback.lyricsAvailable" @click="toggleLyrics">{{playback.lyricsVisible?'关闭歌词':'显示歌词'}}</button></div><div class="volume-control"><div><label for="master-volume">主音量</label><output for="master-volume">{{volumeDraft}}</output></div><input id="master-volume" type="range" min="0" max="125" step="1" :value="volumeDraft" aria-label="主音量" @input="queueVolume" /></div></section>
    <div v-if="playback.nextTitle" class="next">下一首 <strong>{{playback.nextTitle}}</strong></div>
    <section v-show="tab==='songs'" class="content">
      <div class="search"><span>⌕</span><input v-model="query" placeholder="歌名 / 歌手 / 拼音首字母" /><button v-if="query" @click="query=''">×</button></div>
      <div class="chips"><button v-for="item in ['', '华语','粤语','英文','其他']" :key="item" :class="{active:language===item}" @click="language=item">{{item||'全部'}}</button></div>
      <div class="section-title"><h3>{{query?'搜索结果':'热门歌曲'}}</h3><span>{{songs.length}} 首</span></div>
      <div v-if="busy&&songs.length===0" class="empty">正在寻找好歌…</div><div v-else-if="songs.length===0" class="empty">还没有找到歌曲<br><small>请在电脑管理台导入视频或音频</small></div>
      <article v-for="(song,index) in songs" :key="song.id" class="song"><span class="rank">{{String(index+1).padStart(2,'0')}}</span><div><h4>{{song.title}}</h4><p>{{song.artistDisplayName}} · {{song.language}} · {{song.mediaType===0||song.mediaType===2?'MV':'音频'}}<template v-if="song.lyricRelativePath"> · 有歌词</template><template v-if="song.accompanimentAudioRelativePath"> · 有伴奏</template><template v-if="song.hasCustomSlideshow"> · 幻灯片</template></p></div><button class="heart" :class="{on:song.isFavorite}" @click="favorite(song)">♥</button><button class="order" :disabled="!song.isAvailable" @click="order(song)">点歌</button></article>
    </section>
    <section v-show="tab!=='songs'" class="content"><div class="section-title"><h3>{{tab==='mine'?'我的点歌':'已点队列'}}</h3><span>{{(tab==='mine'?myQueue:queue).length}} 首</span></div>
      <div v-if="(tab==='mine'?myQueue:queue).length===0" class="empty">队列还是空的<br><small>从歌库挑一首开始吧</small></div>
      <article v-for="(item,index) in (tab==='mine'?myQueue:queue)" :key="item.id" class="song queue"><span class="rank">{{index+1}}</span><div><h4>{{item.song.title}}</h4><p>{{item.song.artistDisplayName}} · {{item.requestedBy}} 点</p></div><div v-if="item.isMine" class="queue-actions"><button aria-label="上移" @click="move(item,-1)">↑</button><button aria-label="下移" @click="move(item,1)">↓</button><button class="remove" @click="remove(item)">删除</button></div></article>
    </section>
    <nav><button :class="{active:tab==='songs'}" @click="tab='songs'"><span>⌕</span>点歌</button><button :class="{active:tab==='queue'}" @click="tab='queue'"><span>≡</span>队列<i v-if="queue.length">{{queue.length}}</i></button><button :class="{active:tab==='mine'}" @click="tab='mine'"><span>♪</span>我的<i v-if="myQueue.length">{{myQueue.length}}</i></button></nav>
    <transition name="toast"><div v-if="message" class="toast">{{message}}</div></transition>
  </main>
</template>

<style scoped>
.control-trigger{width:48px;height:48px;border:1px solid #ffffff18;border-radius:8px;background:#ffffff0a;color:#c7cada;display:grid;place-content:center;gap:2px;padding:0}
.control-trigger span{font-size:12px;font-weight:700}.control-trigger small{font-size:10px;color:#8f94aa}.control-trigger.open{color:#fff;background:#7251e6;border-color:#9978ff}.control-trigger.open small{color:#e4dcff}
.admin-control{display:grid;gap:8px;margin:10px 0}
.admin-buttons{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:7px}
.admin-control button{border:0;border-radius:10px;min-height:40px;padding:0 8px;background:#7251e6;color:#fff}
.admin-control button.selected{background:#16a277;box-shadow:inset 0 0 0 1px #65dfba}
.admin-control button:disabled{opacity:.45}
.volume-control{display:grid;gap:8px;padding:12px 13px;background:#171a27;border:1px solid #ffffff12;border-radius:8px}
.volume-control>div{display:flex;justify-content:space-between;align-items:center;color:#c7cada;font-size:13px}.volume-control output{min-width:3ch;text-align:right;color:#fff;font-weight:700}
.volume-control input{width:100%;height:24px;margin:0;accent-color:#7251e6}
@media(max-width:360px){.admin-buttons{grid-template-columns:repeat(2,minmax(0,1fr))}}
</style>
