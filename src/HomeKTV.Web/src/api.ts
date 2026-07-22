export interface Song { id:number; title:string; artistDisplayName:string; language:string; durationMs:number; isFavorite:boolean; isAvailable:boolean }
export interface QueueSong { id:number; title:string; artistDisplayName:string }
export interface QueueItem { id:number; songId:number; requestedBy:string; guestSessionId:string; state:number; song:QueueSong }
export interface Session { id:string; nickname:string; isAdministrator:boolean }
export interface Playback { title:string|null; artist:string|null; state:string; nextTitle:string|null }

async function request<T>(url:string, init?:RequestInit):Promise<T> {
  const response=await fetch(url,{...init,headers:{'Content-Type':'application/json',...(init?.headers??{})}})
  if(!response.ok){let message=`请求失败 (${response.status})`;try{message=(await response.json()).error??message}catch{}throw new Error(message)}
  return response.status===204 ? undefined as T : response.json() as Promise<T>
}

export const api={
  createSession:(nickname:string)=>request<Session>('/api/session',{method:'POST',body:JSON.stringify({nickname})}),
  search:(query:string,language='')=>request<Song[]>(`/api/songs?q=${encodeURIComponent(query)}&language=${encodeURIComponent(language)}`),
  state:()=>request<{playback:Playback;queue:QueueItem[]}>('/api/state'),
  enqueue:(songId:number,sessionId:string)=>request<QueueItem>('/api/queue',{method:'POST',body:JSON.stringify({songId,sessionId})}),
  remove:(id:number,sessionId:string)=>request<void>(`/api/queue/${id}?sessionId=${encodeURIComponent(sessionId)}`,{method:'DELETE'}),
  favorite:(id:number,isFavorite:boolean,sessionId:string)=>request<void>(`/api/songs/${id}/favorite`,{method:'POST',body:JSON.stringify({isFavorite,sessionId})})
}

