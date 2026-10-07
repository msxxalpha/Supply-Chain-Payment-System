(()=>{
const fmt=new Intl.DateTimeFormat("fa-IR-u-ca-persian",{year:"numeric",month:"numeric",day:"numeric"});
const months=["فروردین","اردیبهشت","خرداد","تیر","مرداد","شهریور","مهر","آبان","آذر","دی","بهمن","اسفند"],weeks=["ش","ی","د","س","چ","پ","ج"];
function norm(s){return(s||"").replace(/[۰-۹]/g,d=>String("۰۱۲۳۴۵۶۷۸۹".indexOf(d)))}
function g2j(d){const p=fmt.formatToParts(d);return{y:+norm(p.find(x=>x.type==="year").value),m:+norm(p.find(x=>x.type==="month").value),day:+norm(p.find(x=>x.type==="day").value)}}
function j2g(y,m,day){const guess=new Date(Date.UTC(y-621,m-1,day));for(let i=-370;i<=370;i++){const d=new Date(guess.getTime()+i*86400000),p=g2j(d);if(p.y===y&&p.m===m&&p.day===day)return d}throw new Error("invalid")}
function dim(y,m){return Math.round((j2g(m===12?y+1:y,m===12?1:m+1,1)-j2g(y,m,1))/86400000)}
function first(y,m){return(j2g(y,m,1).getUTCDay()+1)%7}
function fa(n){return String(n).replace(/\d/g,d=>"۰۱۲۳۴۵۶۷۸۹"[+d])}
function parse(v){const p=norm(v).replaceAll("-","/").split("/").map(Number);return p.length===3&&p.every(Number.isFinite)&&p[0]>1000&&p[1]>=1&&p[1]<=12&&p[2]>=1&&p[2]<=31?{y:p[0],m:p[1],day:p[2]}:null}

let active=null;

function positionPicker(input,picker){
    const r=input.getBoundingClientRect();
    const gap=7;
    const width=Math.min(300,window.innerWidth-20);
    picker.style.width=width+"px";
    picker.style.left=Math.max(10,Math.min(r.left,window.innerWidth-width-10))+"px";
    const pickerHeight=picker.getBoundingClientRect().height||360;
    const below=r.bottom+gap;
    const above=r.top-gap-pickerHeight;
    picker.style.top=(below+pickerHeight<=window.innerHeight-10?below:Math.max(10,above))+"px";
}

function closePicker(picker){
    if(!picker)return;
    picker.classList.remove("open");
    picker.setAttribute("aria-hidden","true");
    if(active&&active.picker===picker)active=null;
}

function attach(input,trigger){
    let j=parse(input.value)||g2j(new Date());
    const picker=document.createElement("div");
    picker.className="jalali-picker";
    picker.setAttribute("role","dialog");
    picker.setAttribute("aria-hidden","true");
    picker.style.display="none";
    document.body.appendChild(picker);

    function openPicker(){
        const p=parse(input.value);
        if(p)j=p;
        render();
        document.querySelectorAll(".jalali-picker.open").forEach(x=>x!==picker&&closePicker(x));
        picker.style.display="block";
        picker.classList.add("open");
        picker.setAttribute("aria-hidden","false");
        positionPicker(input,picker);
        active={input,picker};
    }

    function render(){
        const total=dim(j.y,j.m),offset=first(j.y,j.m),today=g2j(new Date());
        picker.innerHTML='<div class="jp-head"><button type="button" data-prev aria-label="ماه قبل">‹</button><div class="jp-title">'+months[j.m-1]+' '+fa(j.y)+'</div><button type="button" data-next aria-label="ماه بعد">›</button></div><div class="jp-week">'+weeks.map(x=>'<div>'+x+'</div>').join("")+'</div><div class="jp-grid"></div>';
        const grid=picker.querySelector(".jp-grid");
        for(let i=0;i<offset;i++){const e=document.createElement("button");e.type="button";e.className="jp-cell empty";e.tabIndex=-1;grid.appendChild(e)}
        for(let d=1;d<=total;d++){
            const b=document.createElement("button");b.type="button";b.className="jp-cell";b.textContent=fa(d);
            if(d===j.day)b.classList.add("selected");
            if(today.y===j.y&&today.m===j.m&&today.day===d)b.classList.add("today");
            b.addEventListener("click",()=>{
                j={...j,day:d};
                input.value=j.y+"/"+String(j.m).padStart(2,"0")+"/"+String(j.day).padStart(2,"0");
                input.dispatchEvent(new Event("change",{bubbles:true}));
                closePicker(picker);
            });
            grid.appendChild(b);
        }
        picker.querySelector("[data-prev]").addEventListener("click",()=>{
            j.m--;if(j.m<1){j.m=12;j.y--}
            j.day=Math.min(j.day,dim(j.y,j.m));render();positionPicker(input,picker);
        });
        picker.querySelector("[data-next]").addEventListener("click",()=>{
            j.m++;if(j.m>12){j.m=1;j.y++}
            j.day=Math.min(j.day,dim(j.y,j.m));render();positionPicker(input,picker);
        });
    }

    trigger.addEventListener("click",e=>{e.preventDefault();openPicker()});
    input.addEventListener("focus",()=>{
        const p=parse(input.value);if(p)j=p;openPicker();
    });
    input.addEventListener("keydown",e=>{if(e.key==="Escape")closePicker(picker)});

    window.addEventListener("resize",()=>{if(active&&active.picker===picker&&picker.classList.contains("open"))positionPicker(input,picker)});
    window.addEventListener("scroll",()=>{if(active&&active.picker===picker&&picker.classList.contains("open"))positionPicker(input,picker)},true);
}

document.addEventListener("DOMContentLoaded",()=>{
    document.querySelectorAll("[data-jalali-date]").forEach(input=>{
        const trigger=input.parentElement?.querySelector("[data-jalali-trigger]");
        if(trigger)attach(input,trigger);
    });
    document.addEventListener("click",e=>{
        if(active&&!active.picker.contains(e.target)&&e.target!==active.input){
            closePicker(active.picker);
        }
    });
});
})();