(()=>{
"use strict";

const MONTHS=["فروردین","اردیبهشت","خرداد","تیر","مرداد","شهریور","مهر","آبان","آذر","دی","بهمن","اسفند"];
const WEEKDAYS=["ش","ی","د","س","چ","پ","ج"];
const FA_DIGITS="۰۱۲۳۴۵۶۷۸۹";

function normalizeDigits(value){
    return String(value??"")
        .replace(/[۰-۹]/g,d=>String(FA_DIGITS.indexOf(d)))
        .replace(/[٠-٩]/g,d=>String("٠١٢٣٤٥٦٧٨٩".indexOf(d)));
}
function toFa(value){return String(value).replace(/\d/g,d=>FA_DIGITS[Number(d)]);}

/* Pure Gregorian <-> Jalali conversion. No browser locale/Intl dependency. */
function gregorianToJalali(gy,gm,gd){
    const gdm=[0,31,59,90,120,151,181,212,243,273,304,334];
    let jy=gy>1600?979:0;
    let gy2=gy>1600?gy-1600:gy-621;
    const gy2ForMonth=gm>2?gy2+1:gy2;
    let days=365*gy2
        +Math.floor((gy2ForMonth+3)/4)
        -Math.floor((gy2ForMonth+99)/100)
        +Math.floor((gy2ForMonth+399)/400)
        -80+gd+gdm[gm-1];
    jy+=33*Math.floor(days/12053); days%=12053;
    jy+=4*Math.floor(days/1461); days%=1461;
    if(days>365){jy+=Math.floor((days-1)/365);days=(days-1)%365;}
    const jm=days<186?1+Math.floor(days/31):7+Math.floor((days-186)/30);
    const jd=1+(days<186?days%31:(days-186)%30);
    return {y:jy,m:jm,day:jd};
}

function jalaliToGregorian(jy,jm,jd){
    let gy=jy>979?1600:621;
    let jy2=jy>979?jy-979:jy;
    let days=365*jy2
        +Math.floor(jy2/33)*8
        +Math.floor(((jy2%33)+3)/4)
        +78+jd;
    days += jm<7?(jm-1)*31:((jm-7)*30+186);
    gy+=400*Math.floor(days/146097); days%=146097;
    if(days>36524){
        gy+=100*Math.floor((days-1)/36524);
        days=(days-1)%36524;
        if(days>=365)days++;
    }
    gy+=4*Math.floor(days/1461); days%=1461;
    if(days>365){gy+=Math.floor((days-1)/365);days=(days-1)%365;}
    let gd=days+1;
    const leap=(gy%4===0&&gy%100!==0)||gy%400===0;
    const gdm=[31,leap?29:28,31,30,31,30,31,31,30,31,30,31];
    let gm=1;
    while(gm<=12&&gd>gdm[gm-1]){gd-=gdm[gm-1];gm++;}
    return {y:gy,m:gm,day:gd};
}

function j2gDate(j){
    const g=jalaliToGregorian(j.y,j.m,j.day);
    return new Date(Date.UTC(g.y,g.m-1,g.day));
}
function g2jDate(date){
    return gregorianToJalali(date.getUTCFullYear(),date.getUTCMonth()+1,date.getUTCDate());
}
function daysInMonth(y,m){
    if(m<=6)return 31;
    if(m<=11)return 30;
    const d=jalaliToGregorian(y,m,30);
    const back=gregorianToJalali(d.y,d.m,d.day);
    return back.y===y&&back.m===m&&back.day===30?30:29;
}
function dayOffset(y,m){
    const g=jalaliToGregorian(y,m,1);
    return (new Date(Date.UTC(g.y,g.m-1,g.day)).getUTCDay()+1)%7;
}
function parseJalali(value){
    const normalized=normalizeDigits(value).trim().replace(/[.-]/g,"/");
    const p=normalized.split("/").filter(Boolean);
    if(p.length!==3)return null;
    const y=Number(p[0]),m=Number(p[1]),day=Number(p[2]);
    if(!Number.isInteger(y)||!Number.isInteger(m)||!Number.isInteger(day)||y<1000||m<1||m>12||day<1||day>daysInMonth(y,m))return null;
    return {y,m,day};
}
function formatJalali(j){return String(j.y).padStart(4,"0")+"/"+String(j.m).padStart(2,"0")+"/"+String(j.day).padStart(2,"0");}
function todayJalali(){return g2jDate(new Date());}

let active=null;

function closePicker(state){
    if(!state)return;
    state.picker.classList.remove("open");
    state.picker.setAttribute("aria-hidden","true");
    state.picker.style.display="none";
    state.input.setAttribute("aria-expanded","false");
    if(active===state)active=null;
}

function positionPicker(state){
    if(!state||!state.picker.classList.contains("open"))return;
    const inputRect=state.input.getBoundingClientRect();
    const picker=state.picker;
    const margin=10;
    const gap=7;
    const width=Math.min(320,Math.max(260,window.innerWidth-margin*2));
    picker.style.width=width+"px";
    picker.style.maxWidth="calc(100vw - "+(margin*2)+"px)";

    const measuredHeight=picker.offsetHeight||350;
    const rtl=document.documentElement.dir.toLowerCase()==="rtl";
    let left=rtl?inputRect.right-width:inputRect.left;
    left=Math.max(margin,Math.min(left,window.innerWidth-width-margin));

    const below=inputRect.bottom+gap;
    const above=inputRect.top-gap-measuredHeight;
    let top;
    if(below+measuredHeight<=window.innerHeight-margin) top=below;
    else if(above>=margin) top=above;
    else top=Math.max(margin,Math.min(below,window.innerHeight-measuredHeight-margin));

    picker.style.left=Math.round(left)+"px";
    picker.style.top=Math.round(top)+"px";
}

function attach(input,trigger){
    if(input.dataset.jalaliInitialized==="1")return;
    input.dataset.jalaliInitialized="1";
    input.type="text";
    input.inputMode="numeric";
    input.autocomplete="off";
    input.spellcheck=false;
    input.setAttribute("role","combobox");
    input.setAttribute("aria-haspopup","dialog");
    input.setAttribute("aria-expanded","false");

    let current=parseJalali(input.value)||todayJalali();
    const picker=document.createElement("div");
    picker.className="jalali-picker";
    picker.setAttribute("role","dialog");
    picker.setAttribute("aria-label","انتخاب تاریخ شمسی");
    picker.setAttribute("aria-hidden","true");
    picker.style.display="none";
    document.body.appendChild(picker);
    const state={input,trigger,picker};

    function render(){
        const total=daysInMonth(current.y,current.m);
        const offset=dayOffset(current.y,current.m);
        const today=todayJalali();
        picker.innerHTML="";
        const head=document.createElement("div");
        head.className="jp-head";
        const prev=document.createElement("button");
        prev.type="button";prev.setAttribute("data-prev","");prev.setAttribute("aria-label","ماه قبل");prev.textContent="‹";
        const title=document.createElement("div");
        title.className="jp-title";
        title.textContent=MONTHS[current.m-1]+" "+toFa(current.y);
        const next=document.createElement("button");
        next.type="button";next.setAttribute("data-next","");next.setAttribute("aria-label","ماه بعد");next.textContent="›";
        head.append(prev,title,next);
        picker.appendChild(head);

        const week=document.createElement("div");
        week.className="jp-week";
        for(const day of WEEKDAYS){const cell=document.createElement("div");cell.textContent=day;week.appendChild(cell);}
        picker.appendChild(week);

        const grid=document.createElement("div");
        grid.className="jp-grid";
        for(let i=0;i<offset;i++){
            const empty=document.createElement("span");empty.className="jp-cell empty";empty.setAttribute("aria-hidden","true");grid.appendChild(empty);
        }
        for(let d=1;d<=total;d++){
            const b=document.createElement("button");
            b.type="button";
            b.className="jp-cell";
            b.textContent=toFa(d);
            b.setAttribute("aria-label",formatJalali({y:current.y,m:current.m,day:d}));
            if(current.y===today.y&&current.m===today.m&&d===today.day)b.classList.add("today");
            if(current.day===d)b.classList.add("selected");
            b.addEventListener("click",()=>{
                current={y:current.y,m:current.m,day:d};
                input.value=formatJalali(current);
                input.dispatchEvent(new Event("change",{bubbles:true}));
                input.dispatchEvent(new Event("input",{bubbles:true}));
                input.setAttribute("aria-expanded","false");
                closePicker(state);
            });
            grid.appendChild(b);
        }
        picker.appendChild(grid);

        prev.addEventListener("click",e=>{
            e.preventDefault();
            if(current.m===1){current.m=12;current.y--;}else current.m--;
            current.day=Math.min(current.day,daysInMonth(current.y,current.m));
            render();positionPicker(state);
        });
        next.addEventListener("click",e=>{
            e.preventDefault();
            if(current.m===12){current.m=1;current.y++;}else current.m++;
            current.day=Math.min(current.day,daysInMonth(current.y,current.m));
            render();positionPicker(state);
        });
    }

    function openPicker(){
        if(active&&active!==state)closePicker(active);
        const parsed=parseJalali(input.value);
        if(parsed)current=parsed;
        render();
        picker.style.display="block";
        picker.classList.add("open");
        picker.setAttribute("aria-hidden","false");
        input.setAttribute("aria-expanded","true");
        active=state;
        positionPicker(state);
        requestAnimationFrame(()=>positionPicker(state));
    }

    trigger.addEventListener("click",e=>{e.preventDefault();e.stopPropagation();openPicker();});
    input.addEventListener("focus",openPicker);
    input.addEventListener("keydown",e=>{
        if(e.key==="Escape"){e.preventDefault();closePicker(state);return;}
        if(e.key==="ArrowDown"&&!picker.classList.contains("open")){e.preventDefault();openPicker();}
    });
    input.addEventListener("blur",()=>{
        const parsed=parseJalali(input.value);
        if(parsed)input.value=formatJalali(parsed);
    });

    state.open=openPicker;
}

function init(){
    document.querySelectorAll("[data-jalali-date]").forEach(input=>{
        const trigger=input.parentElement?.querySelector("[data-jalali-trigger]");
        if(trigger)attach(input,trigger);
    });
}
document.addEventListener("DOMContentLoaded",init);
if(document.readyState!=="loading")init();

document.addEventListener("mousedown",e=>{
    if(!active)return;
    if(active.picker.contains(e.target)||e.target===active.input||e.target===active.trigger)return;
    closePicker(active);
});
window.addEventListener("resize",()=>{if(active)positionPicker(active);},{passive:true});
window.addEventListener("scroll",()=>{if(active)positionPicker(active);},true);
})();
