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
document.addEventListener("DOMContentLoaded",()=>{
 document.querySelectorAll("[data-confirm]").forEach(e=>e.addEventListener("click",x=>{if(!confirm(e.dataset.confirm||"آیا مطمئن هستید؟"))x.preventDefault();}));
 const modal=document.getElementById("missingReferencesModal");if(modal&&modal.dataset.showOnLoad==="true"&&window.bootstrap){bootstrap.Modal.getOrCreateInstance(modal).show();}
});
function normalizeFa(value){return (value||"").trim().replace(/ي/g,"ی").replace(/ك/g,"ک").toLocaleLowerCase("fa-IR");}
document.addEventListener("DOMContentLoaded",()=>{
 document.querySelectorAll("[data-filter-select]").forEach(box=>{
  const input=box.querySelector("[data-select-filter-input]"),select=box.querySelector("[data-select-filter-target]");
  if(!input||!select)return;
  const options=[...select.options].map(option=>({option,text:normalizeFa(option.textContent)}));
  input.addEventListener("input",()=>{
   const q=normalizeFa(input.value);
   options.forEach(x=>x.option.hidden=!!q&&!x.text.includes(q));
   const visible=options.filter(x=>!x.option.hidden);
   if(select.selectedOptions.length&&select.selectedOptions[0].hidden){
    if(visible.length===1)select.value=visible[0].option.value;
   }
  });
  input.addEventListener("keydown",e=>{if(e.key==="Escape"){input.value="";input.dispatchEvent(new Event("input"));input.blur();}});
  select.addEventListener("change",()=>{const selected=select.selectedOptions[0];if(selected&&!input.value)input.placeholder=selected.textContent.trim();});
 });
});
