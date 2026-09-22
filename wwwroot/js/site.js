document.addEventListener("DOMContentLoaded",()=>{
 document.querySelectorAll("table.table").forEach(t=>{
  t.querySelectorAll("thead th").forEach((h,i)=>{
   h.style.cursor="pointer";h.title="مرتب‌سازی";
   h.addEventListener("click",()=>{
    const rows=[...t.querySelectorAll("tbody tr")];const asc=h.dataset.asc!=="1";
    rows.sort((a,b)=>a.cells[i].innerText.localeCompare(b.cells[i].innerText,"fa",{numeric:true,sensitivity:"base"}));
    if(!asc)rows.reverse();rows.forEach(r=>t.tBodies[0].appendChild(r));h.dataset.asc=asc?"1":"0";
   });
  });
 });
 document.querySelectorAll("[data-table-filter]").forEach(input=>input.addEventListener("input",()=>{
  const t=document.querySelector(input.dataset.tableFilter),q=input.value.trim().toLocaleLowerCase("fa-IR");
  if(t)t.querySelectorAll("tbody tr").forEach(r=>r.style.display=r.innerText.toLocaleLowerCase("fa-IR").includes(q)?"":"none");
 }));
});
document.addEventListener("DOMContentLoaded",()=>document.querySelectorAll("[data-confirm]").forEach(e=>e.addEventListener("click",x=>{if(!confirm(e.dataset.confirm||"آیا مطمئن هستید؟"))x.preventDefault();})));