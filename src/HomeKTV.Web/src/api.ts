export interface Song { id:number; title:string; artistDisplayName:string; language:string; durationMs:number; isFavorite:boolean; isAvailable:boolean; mediaType:number; lyricRelativePath:string|null; accompanimentAudioRelativePath:string|null; hasCustomSlideshow:boolean }
export interface QueueSong { id:number; title:string; artistDisplayName:string; mediaType:number; hasLyrics:boolean; hasAccompaniment:boolean; hasCustomSlideshow:boolean }
export interface QueueItem { id:number; songId:number; requestedBy:string; requestedAt:string; position:number; isPinned:boolean; state:number; errorMessage:string|null; isMine:boolean; song:QueueSong }
export interface Session { id:string; nickname:string; isAdministrator:boolean; accessToken:string }
export interface Playback { queueItemId:number|null; title:string|null; artist:string|null; state:string; nextTitle:string|null; lyricsVisible:boolean; lyricsAvailable:boolean; audioMode:'Original'|'Accompaniment'; canUseAccompaniment:boolean; volume:number }
export type PlaybackControlCommand='togglePause'|'restart'|'skip'|'original'|'accompaniment'|'lyricsIncreaseFiveSeconds'|'lyricsDecreaseFiveSeconds'

function sessionHeaders(session?:Session|null):Record<string,string> {
  return session?{'X-HomeKTV-Session':session.id,'X-HomeKTV-Token':session.accessToken}:{}
}

async function request<T>(url:string, init?:RequestInit, session?:Session|null):Promise<T> {
  const response=await fetch(url,{...init,headers:{'Content-Type':'application/json',...sessionHeaders(session),...(init?.headers??{})}})
  if(!response.ok){let message='请求失败 ('+response.status+')';try{message=(await response.json()).error??message}catch{/* 非 JSON 错误页使用 HTTP 状态信息 */}throw new Error(message)}
  return response.status===204 ? undefined as T : response.json() as Promise<T>
}

export const api={
  createSession:(nickname:string)=>request<Session>('/api/session',{method:'POST',body:JSON.stringify({nickname})}),
  search:(query:string,language:string,session:Session)=>request<Song[]>('/api/songs?q='+encodeURIComponent(query)+'&language='+encodeURIComponent(language),undefined,session),
  state:(session:Session)=>request<{playback:Playback;queue:QueueItem[]}>('/api/state',undefined,session),
  enqueue:(songId:number,session:Session)=>request<QueueItem>('/api/queue',{method:'POST',body:JSON.stringify({songId})},session),
  remove:(id:number,session:Session)=>request<void>('/api/queue/'+id,{method:'DELETE'},session),
  move:(id:number,direction:-1|1,session:Session)=>request<void>('/api/queue/'+id+'/move',{method:'POST',body:JSON.stringify({direction})},session),
  favorite:(id:number,isFavorite:boolean,session:Session)=>request<void>('/api/songs/'+id+'/favorite',{method:'POST',body:JSON.stringify({isFavorite})},session),
  setLyricsVisible:(visible:boolean,session:Session)=>request<void>('/api/playback/lyrics',{method:'POST',body:JSON.stringify({visible})},session),
  playbackControl:(command:PlaybackControlCommand,session:Session)=>request<void>('/api/playback/control',{method:'POST',body:JSON.stringify({command})},session),
  setVolume:(volume:number,session:Session)=>request<void>('/api/playback/volume',{method:'POST',body:JSON.stringify({volume})},session)
}
