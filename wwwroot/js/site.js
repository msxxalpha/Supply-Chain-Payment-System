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

document.addEventListener("DOMContentLoaded",()=>{document.querySelectorAll("[data-edit-toggle]").forEach(btn=>btn.addEventListener("click",()=>{const row=document.getElementById(btn.dataset.editToggle);if(!row)return;const open=row.classList.toggle("is-open");row.style.display=open?"table-row":"none";btn.textContent=open?"بستن ویرایش":"ویرایش";}));document.querySelectorAll(".edit-row").forEach(row=>row.style.display="none");});

(function(){
 const ones=["","یک","دو","سه","چهار","پنج","شش","هفت","هشت","نه","ده","یازده","دوازده","سیزده","چهارده","پانزده","شانزده","هفده","هجده","نوزده"];
 const tens=["","","بیست","سی","چهل","پنجاه","شصت","هفتاد","هشتاد","نود"];
 const hundreds=["","صد","دویست","سیصد","چهارصد","پانصد","ششصد","هفتصد","هشتصد","نهصد"];
 const scales=["","هزار","میلیون","میلیارد","تریلیون","کوادریلیون","کوینتیلیون"];
 function normalizeDigits(v){return String(v??"").replace(/[۰-۹]/g,d=>String("۰۱۲۳۴۵۶۷۸۹".indexOf(d))).replace(/[٠-٩]/g,d=>String("٠١٢٣٤٥٦٧٨٩".indexOf(d)));}
 function tri(n){
  const a=[];
  if(n>=100)a.push(hundreds[Math.floor(n/100)]),n%=100;
  if(n>=20)a.push(tens[Math.floor(n/10)]),n%=10;
  if(n>0)a.push(ones[n]);
  return a.join(" و ");
 }
 function words(n){
  n=Math.floor(n);
  if(n===0)return "صفر";
  const parts=[];
  let i=0;
  while(n>0){
   const part=n%1000;
   if(part)parts.unshift(tri(part)+(scales[i]?" "+scales[i]:""));
   n=Math.floor(n/1000);i++;
  }
  return parts.join(" و ");
 }
 function parseAmount(v){
  let s=normalizeDigits(v).replace(/[٬,]/g,"").replace(/٫/g,".").replace(/\s+/g,"").replace(/[^0-9.]/g,"");
  const dot=s.indexOf(".");
  if(dot>=0)s=s.slice(0,dot+1)+s.slice(dot+1).replace(/\./g,"");
  const n=Number(s);
  return Number.isFinite(n)?n:null;
 }
 function formatAmount(v){
  let s=normalizeDigits(v).replace(/[٬,]/g,"").replace(/٫/g,".").replace(/\s+/g,"").replace(/[^0-9.]/g,"");
  if(!s)return "";
  const dot=s.indexOf(".");
  let integer=dot>=0?s.slice(0,dot):s;
  let fraction=dot>=0?s.slice(dot+1).slice(0,2):"";
  integer=integer.replace(/^0+(?=\d)/,"");
  integer=integer.replace(/\B(?=(\d{3})+(?!\d))/g,",");
  return integer+(dot>=0?"."+fraction:"");
 }
 function renderWords(input){
  const target=input.parentElement?.querySelector("[data-money-words]");
  if(!target)return;
  const n=parseAmount(input.value);
  if(n===null){target.textContent="مبلغ به حروف پس از ورود نمایش داده می‌شود.";return;}
  const rounded=Math.round(n*100)/100;
  const integer=Math.floor(Math.abs(rounded));
  const fraction=Math.round((Math.abs(rounded)-integer)*100);
  let text=(rounded<0?"منفی ":"")+words(integer);
  if(fraction>0)text+=" و "+words(fraction)+" صدم";
  target.textContent=text+" ریال";
 }
 document.addEventListener("DOMContentLoaded",()=>{
  document.querySelectorAll("[data-money-input]").forEach(input=>{
   input.value=formatAmount(input.value);
   renderWords(input);
   input.addEventListener("input",()=>{
    input.value=formatAmount(input.value);
    renderWords(input);
   });
   input.addEventListener("blur",()=>{
    input.value=formatAmount(input.value);
    renderWords(input);
   });
  });
 });
})();
