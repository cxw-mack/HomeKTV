<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as signalR from '@microsoft/signalr'
import { api, type Playback, type QueueItem, type Session, type Song } from './api'

type Tab='songs'|'queue'|'mine'
const session=ref<Session|null>(null), nickname=ref(localStorage.getItem('homektv-nickname')??'')
const query=ref(''), language=ref(''), songs=ref<Song[]>([]), queue=ref<QueueItem[]>([])
const playback=ref<Playback>({title:null,artist:null,state:'Idle',nextTitle:null}), tab=ref<Tab>('songs')
const busy=ref(false), message=ref(''), online=ref(navigator.onLine)
let connection:signalR.HubConnection|null=null, searchTimer=0
const myQueue=computed(()=>queue.value.filter(x=>x.guestSessionId===session.value?.id))

async function enter(){if(!nickname.value.trim())return;busy.value=true;try{session.value=await api.createSession(nickname.value.trim());localStorage.setItem('homektv-nickname',nickname.value.trim());await load();await connect()}catch(e){notice(e)}finally{busy.value=false}}
async function load(){const state=await api.state();queue.value=state.queue;playback.value=state.playback;await search()}
async function search(){busy.value=true;try{songs.value=await api.search(query.value,language.value)}catch(e){notice(e)}finally{busy.value=false}}
async function order(song:Song){if(!session.value)return;try{await api.enqueue(song.id,session.value.id);message.value=`已点《${song.title}》`;setTimeout(()=>message.value='',1800)}catch(e){notice(e)}}
async function remove(item:QueueItem){if(!session.value||!confirm(`删除《${item.song.title}》？`))return;try{await api.remove(item.id,session.value.id);queue.value=queue.value.filter(x=>x.id!==item.id)}catch(e){notice(e)}}
async function favorite(song:Song){if(!session.value)return;try{song.isFavorite=!song.isFavorite;await api.favorite(song.id,song.isFavorite,session.value.id)}catch(e){song.isFavorite=!song.isFavorite;notice(e)}}
function notice(error:unknown){message.value=error instanceof Error?error.message:'操作失败，请稍后重试';setTimeout(()=>message.value='',2800)}
async function connect(){if(connection)return;connection=new signalR.HubConnectionBuilder().withUrl('/hub').withAutomaticReconnect([0,1000,3000,5000]).build();connection.on('queueChanged',(items:QueueItem[])=>queue.value=items);connection.onreconnecting(()=>online.value=false);connection.onreconnected(()=>{online.value=true;void load()});try{await connection.start();online.value=true}catch{online.value=false}}
watch([query,language],()=>{clearTimeout(searchTimer);searchTimer=window.setTimeout(()=>void search(),250)})
onMounted(()=>{window.addEventListener('online',()=>online.value=true);window.addEventListener('offline',()=>online.value=false)})
onBeforeUnmount(()=>{void connection?.stop()})
</script>

<template>
  <main v-if="!session" class="welcome">
    <div class="brand-mark">H</div><p class="eyebrow">HOME PARTY · 随手点歌</p><h1>今晚，想唱什么？</h1>
    <p class="muted">连接家庭 Wi-Fi 后输入昵称，就可以一起排歌。</p>
    <form class="join" @submit.prevent="enter"><label>你的昵称</label><input v-model="nickname" maxlength="20" autofocus placeholder="例如：小夏" /><button :disabled="busy||!nickname.trim()">{{busy?'正在加入…':'进入点歌台'}}</button></form>
  </main>
  <main v-else class="shell">
    <header><div><p class="eyebrow">HOMEKTV</p><h1>嗨，{{session.nickname}}</h1></div><span class="status" :class="{off:!online}"><i></i>{{online?'已连接':'重连中'}}</span></header>
    <section class="now"><div class="cover"><span>♫</span></div><div class="now-copy"><p>正在播放</p><h2>{{playback.title||'等待第一首歌'}}</h2><span>{{playback.artist||'点一首喜欢的歌吧'}}</span></div><div class="pulse"><i></i><i></i><i></i></div></section>
    <div v-if="playback.nextTitle" class="next">下一首 <strong>{{playback.nextTitle}}</strong></div>
    <section v-show="tab==='songs'" class="content">
      <div class="search"><span>⌕</span><input v-model="query" placeholder="歌名 / 歌手 / 拼音首字母" /><button v-if="query" @click="query=''">×</button></div>
      <div class="chips"><button v-for="item in ['', '华语','粤语','英文','其他']" :key="item" :class="{active:language===item}" @click="language=item">{{item||'全部'}}</button></div>
      <div class="section-title"><h3>{{query?'搜索结果':'热门歌曲'}}</h3><span>{{songs.length}} 首</span></div>
      <div v-if="busy&&songs.length===0" class="empty">正在寻找好歌…</div><div v-else-if="songs.length===0" class="empty">还没有找到歌曲<br><small>请在电脑管理台导入 MV</small></div>
      <article v-for="(song,index) in songs" :key="song.id" class="song"><span class="rank">{{String(index+1).padStart(2,'0')}}</span><div><h4>{{song.title}}</h4><p>{{song.artistDisplayName}} · {{song.language}}</p></div><button class="heart" :class="{on:song.isFavorite}" @click="favorite(song)">♥</button><button class="order" :disabled="!song.isAvailable" @click="order(song)">点歌</button></article>
    </section>
    <section v-show="tab!=='songs'" class="content"><div class="section-title"><h3>{{tab==='mine'?'我的点歌':'已点队列'}}</h3><span>{{(tab==='mine'?myQueue:queue).length}} 首</span></div>
      <div v-if="(tab==='mine'?myQueue:queue).length===0" class="empty">队列还是空的<br><small>从歌库挑一首开始吧</small></div>
      <article v-for="(item,index) in (tab==='mine'?myQueue:queue)" :key="item.id" class="song queue"><span class="rank">{{index+1}}</span><div><h4>{{item.song.title}}</h4><p>{{item.song.artistDisplayName}} · {{item.requestedBy}} 点</p></div><button v-if="item.guestSessionId===session.id" class="remove" @click="remove(item)">删除</button></article>
    </section>
    <nav><button :class="{active:tab==='songs'}" @click="tab='songs'"><span>⌕</span>点歌</button><button :class="{active:tab==='queue'}" @click="tab='queue'"><span>≡</span>队列<i v-if="queue.length">{{queue.length}}</i></button><button :class="{active:tab==='mine'}" @click="tab='mine'"><span>♪</span>我的<i v-if="myQueue.length">{{myQueue.length}}</i></button></nav>
    <transition name="toast"><div v-if="message" class="toast">{{message}}</div></transition>
  </main>
</template>

