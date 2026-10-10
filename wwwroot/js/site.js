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


/* Freeze table header rows during vertical scrolling on data-entry/maintenance pages only. */
(function () {
    function initFrozenTableHeaders() {
        if (!document.body.classList.contains("freeze-table-headers")) return;

        const tables = Array.from(document.querySelectorAll(".table-responsive table, .table-scroll table"))
            .filter(table => table.querySelector("thead") && !table.closest(".modal"));
        if (!tables.length) return;

        const floating = document.createElement("div");
        floating.className = "sticky-table-head-clone";
        floating.setAttribute("aria-hidden", "true");
        document.body.appendChild(floating);

        let activeTable = null;
        let activeThead = null;
        let originalTheadVisibility = "";
        let cloneTable = null;
        let scheduled = false;

        function resetActive() {
            if (activeThead) activeThead.style.visibility = originalTheadVisibility;
            activeTable = null;
            activeThead = null;
            cloneTable = null;
            floating.replaceChildren();
            floating.style.display = "none";
        }

        function updateClone(table, wrapper, top) {
            const thead = table.querySelector("thead");
            if (activeTable !== table) {
                resetActive();
                activeTable = table;
                activeThead = thead;
                originalTheadVisibility = thead.style.visibility;
                cloneTable = table.cloneNode(false);
                cloneTable.removeAttribute("id");
                cloneTable.classList.add("sticky-table-head-copy");
                cloneTable.style.margin = "0";
                cloneTable.style.tableLayout = "fixed";
                const clonedHead = thead.cloneNode(true);
                clonedHead.querySelectorAll("[id]").forEach(node => node.removeAttribute("id"));
                cloneTable.appendChild(clonedHead);
                floating.replaceChildren(cloneTable);
            }

            const wrapperRect = wrapper.getBoundingClientRect();
            const tableRect = table.getBoundingClientRect();
            const headerCells = Array.from(thead.querySelectorAll("th"));
            const clonedCells = Array.from(cloneTable.querySelectorAll("thead th"));
            const headerHeight = thead.getBoundingClientRect().height;

            clonedCells.forEach((cell, index) => {
                if (!headerCells[index]) return;
                const width = headerCells[index].getBoundingClientRect().width;
                cell.style.width = width + "px";
                cell.style.minWidth = width + "px";
                cell.style.maxWidth = width + "px";
            });

            cloneTable.style.width = tableRect.width + "px";
            cloneTable.style.minWidth = tableRect.width + "px";
            cloneTable.style.maxWidth = "none";
            cloneTable.style.transform = "translateX(" + (tableRect.left - wrapperRect.left) + "px)";
            floating.style.top = top + "px";
            floating.style.left = wrapperRect.left + "px";
            floating.style.width = wrapper.clientWidth + "px";
            floating.style.height = headerHeight + "px";
            floating.style.display = "block";
            thead.style.visibility = "hidden";
        }

        function update() {
            scheduled = false;
            const topbar = document.querySelector(".app-topbar");
            const topbarBottom = topbar ? topbar.getBoundingClientRect().bottom : 0;
            let candidate = null;

            for (const table of tables) {
                if (!table.isConnected) continue;
                const thead = table.querySelector("thead");
                const wrapper = table.closest(".table-responsive, .table-scroll") || table.parentElement;
                if (!thead || !wrapper) continue;

                const tableRect = table.getBoundingClientRect();
                const wrapperRect = wrapper.getBoundingClientRect();
                if (tableRect.width <= 0 || tableRect.height <= 0 || wrapperRect.width <= 0) continue;
                const headerHeight = thead.getBoundingClientRect().height;
                const stickyTop = Math.max(topbarBottom, wrapperRect.top);
                if (headerHeight <= 0 || tableRect.top >= stickyTop || tableRect.bottom <= stickyTop + headerHeight ||
                    wrapperRect.bottom <= stickyTop + headerHeight) continue;

                candidate = { table, wrapper, top: stickyTop };
                break;
            }

            if (!candidate) {
                resetActive();
                return;
            }
            updateClone(candidate.table, candidate.wrapper, candidate.top);
        }

        function scheduleUpdate() {
            if (scheduled) return;
            scheduled = true;
            window.requestAnimationFrame(update);
        }

        document.addEventListener("scroll", scheduleUpdate, true);
        window.addEventListener("resize", scheduleUpdate);
        window.addEventListener("orientationchange", scheduleUpdate);
        scheduleUpdate();
    }

    if (document.readyState === "loading")
        document.addEventListener("DOMContentLoaded", initFrozenTableHeaders, { once: true });
    else
        initFrozenTableHeaders();
})();
