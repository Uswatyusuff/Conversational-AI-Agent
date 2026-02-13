let sessionId = localStorage.getItem("sessionId");
if(!sessionId){
  sessionId = crypto.randomUUID();
  localStorage.setItem("sessionId", sessionId);
}