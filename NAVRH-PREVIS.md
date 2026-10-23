# Previs — návrh samostatného programu

Stav: funkční prototyp 0.2.1, 24. 10. 2026. Aktuální implementovaný rozsah a omezení popisuje anglický `README.md`, ověřené kontroly `VALIDATION.md`.

Po připomínce k chybějícím klíčům pohybu po křivce má dráha vlastní stopu `Alex · Path` a časové klíče `P1`, `P2`, `P3` pro jednotlivé body. Tažení klíče mění skutečný čas příjezdu do daného bodu, podporuje změnu tempa mezi body a ukládá se s projektem. Výchozí scéna používá neutrální prostředí bez zapnutého směrového světla; to lze přidat volbou `Environment → Directional light`.

Petr schválil tmavý vizuální směr a zadal první funkční prototyp v Godotu pro stavbu scény, import mocapu a pohyb kamery. **Veškeré uživatelské rozhraní výsledné aplikace musí být anglicky**, včetně dialogů, chyb, nápovědy a názvů ovládacích prvků. České názvy v původním návrhu níže jsou pouze popis specifikace, nikoli texty k převzetí do aplikace.

### Implementovaný průřez 0.2.1

Aktuální verze rozšiřuje základ 0.1.0 o více nezávislých kamer, navazující BVH klipy s prolnutím a pohyb postav po nakreslené dráze. Dodává se Godot **4.7.2 .NET / C# / .NET 10**, primárně pro Windows x64. Následující dlouhodobý návrh nadále obsahuje plánované části; není soupisem již hotových funkcí.

- **Kamery:** úvodní scéna má Camera A, B a C, každou s vlastními klíči a ohniskem. Aktivní kamera se vybírá nad náhledem, v knihovně Cameras nebo kliknutím na její stopu. Lze ji přidat z pracovního pohledu, duplikovat, přejmenovat a odebrat při zachování alespoň jedné kamery. Klíče, předvolby, pilotování a čistý PNG export pracují s aktivní kamerou nad společnou akcí; střih kamer ani hromadný videoexport zatím nejsou hotové.
- **Lidský BVH mocap:** další import přidá klip do stopy, tělo klipu se táhne po ose a pravý okraj mění délku. Překrytí sousedních klipů řídí prolnutí pózy s průběžně navazujícím kořenovým posunem. Motion inspector nabízí Start, Duration, Source offset, Blend in, Speed, Loop source, In-place clip a Source metres/unit. Změna Blend in zároveň umístí začátek klipu vůči konci předchozího. Importované FBX/GLB modely si vybírají jednu vlastní nativní animaci; víceklipová BVH stopa se na ně zatím nevztahuje.
- **Dráhy:** Draw path otevře zadávání bodů klikáním na rovinu země; Finish path nebo Enter dokončí, Esc zruší a zachová původní dráhu. Path inspector nabízí začátek, dobu průchodu a orientaci po směru. Dráha potlačí vodorovný kořenový posun lidského BVH, aby se pohyb nesčítal dvakrát. Změna základní pozice objektu posouvá celou dráhu. Rychlost mocapu a dobu průchodu zatím sladí uživatel; automatické kontakty ani IK nohou nejsou implementované.
- **Neutrální světlo:** výchozí scéna i náhledy knihovny používají šedé hemisférické okolní osvětlení a odrazy. Žádné směrové světlo není ve výchozím stavu aktivní. Environment → Directional light přidává volitelné směrové světlo se stíny. Jde o závazný výchozí pracovní režim: obyčejné rozmístění objektů nemá vyžadovat nastavování světel a přepalovat jejich barvy.
- **Projekt:** verze 2 uchovává všechny kamery, aktivní výběr kamery, BVH klipy, dráhy a stav volitelného světla. Ukládání kopíruje všechny použité BVH a podporované modely do sousedního adresáře `.assets`; předchozí JSON při přepsání zůstává jako `.previs.bak`. Verze 1 se převádí na jednu kameru a jeden klip na dříve animovaného člověka. Připravovaná distribuce: `build/Previs-0.2.1-Windows/`.

Zvířecí retargeting, dodaná animovaná zvířata, referenční video, vodicí video a hloubkové/hranové/maskové průchody jsou další práce. Pes v základní knihovně zůstává statickou rekvizitou. macOS a Linux zatím nejsou sestavené a ověřené. Rozsah původní přejímací scény níže tedy ještě není splněn.

## 1. Co stavíme

Samostatný desktopový nástroj pro **previz filmového záběru jako podklad pro generování AI videa**. Uživatel načrtne prostředí, rozmístí připravené lidi, zvířata a rekvizity, přiřadí pohyby z knihovny nebo importovaného mocapu, naplánuje jejich dráhy a nastaví kameru. Výstupem jsou vodicí videa, první snímek a přesný popis akce s časy.

Modely dodávané s programem už jsou zarigované. Běžná práce probíhá pomocí klipů pohybů, drah a kamerových předvoleb. Vlastní rigování, tvorba animace po kostech, detailní modelování a plný střihový program nejsou součástí navrhovaného produktu.

Základní postup:

**Postavit prostor → přidat aktéry → přiřadit mocap → určit dráhy a časování → nastavit kameru → exportovat podklady pro AI.**

## 2. Předloha: Topview 3D Builder

Podkladem je původní studie `C:\Users\Petr\Documents\newGIS\dokumentace\PREVIS-PLAN.md` a živá prohlídka 3D Builderu v Petrově přihlášeném Chromu 24. 10. 2026. Prohlídka zahrnovala knihovny, vybranou postavu, nastavení klipu, kamery, pohyby kamer, rekvizity a exportní dialog. Nebyl spuštěn export ani generování a nebyly upravovány objekty nebo klipy projektu.

Ve skutečném rozhraní byly ověřeny:

- úzká levá lišta **Objects / Characters / Props / Cameras / Camera moves / Motions**;
- vedle ní knihovna se dvěma sloupci náhledů, kategoriemi a hledáním;
- velký 3D pohled se štítky aktérů a kamerami jako objekty;
- připnutý náhled vybrané kamery vpravo, pod ním kontextové vlastnosti;
- časová osa s klipy postav i pohybů kamer, transformacemi, přichytáváním a automatickými klíči;
- u vybraného pohybu rychlost, smyčka a pohyb na místě;
- kamerové předvolby, ohnisko v mm, Look-at a Follow;
- export obrázku nebo videa s výběrem kamery, snímků, poměru stran a rozlišení. V otevřeném dialogu byly nabízeny 720p, 1080p a 1440p.

**Toto rozvržení a jednoduchost jsou hlavní předloha.** Rozšíření Previsu budou ve stejné pracovní ploše: import mocapu, animovaná zvířata, přehlednější knihovna prostředí a export průchodů pro AI. Srovnání vychází z prohlédnutých částí Topview; není tvrzením o všech schopnostech celé služby.

## 3. Doporučená technologie

**Godot 4, varianta .NET, hlavní jazyk C#.** Vlastní rozhraní aplikace postavíme pomocí Godot `Control` a kontejnerů. Stabilní verzi enginu a exportních šablon uzamkneme po ověření prvního technického průřezu.

Godot oficiálně podporuje C# na Windows, macOS a Linuxu. Nabízí 3D vykreslování, kosterní animace a běhové načítání uživatelských GLB/glTF souborů; nemusí se kvůli tomu spouštět vývojový editor. [C# a platformy](https://docs.godotengine.org/en/stable/tutorials/scripting/c_sharp/index.html), [načítání souborů za běhu](https://docs.godotengine.org/en/stable/tutorials/io/runtime_file_loading_and_saving.html).

| Varianta | Posouzení pro tento projekt |
|---|---|
| **Godot + C#** | Doporučený základ pro vlastní 3D desktopový nástroj. UI, časová osa, výběr, táhla a exportní průchody budou naše aplikační vrstva. |
| **Tauri + TypeScript + Three.js** | Silná alternativa, pokud bude hlavní prioritou převzetí stávajícího JS z NewGIS. Pro nový samostatný program preferuji společný 3D engine; Tauri navíc používá rozdílné webview podle OS. |
| **Unity + C#** | Technicky vhodné pro lidi i zvířata. V tomto rozsahu preferuji otevřený základ Godotu a kontrolu nad jeho distribucí. |
| **Unreal + C++ / Blueprinty** | Vhodný pro rozsáhlou realistickou virtuální produkci. Pro navržený jednoduchý previz přináší širší výrobní prostředí, než nyní potřebujeme. |

Toto je technické doporučení, nikoli výsledek hotového výkonnostního benchmarku. Three.js umí zvířata i přesné nastavení animačního času; rozhodnutí pro Godot není založené na opaku. Tauri používá WebView2 na Windows a WebKit na macOS/Linuxu. [Tauri webview](https://v2.tauri.app/reference/webview-versions/). Godot je dostupný pod MIT licencí. [Licence enginu](https://godotengine.org/license/).

Doplňkové nástroje:

- **FFmpeg**: snímková mezipaměť referenčního videa a kódování výstupů.
- **Blender + Python**: příprava a kontrola dodávaných knihoven mimo běžící aplikaci. Běžné používání Previsu nebude vyžadovat Blender.
- **GLB**: sjednocený interní formát modelů a připravených animačních assetů.

Původní JavaScript bude reference pro převod matematických funkcí a chování, nikoli přímo spuštěná vrstva nové aplikace. Konkrétní zdrojové soubory zatím nebyly auditovány pro převzetí. Studie, obsahové podklady a koncept exportních metadat se využijí; propojení s GIS Library nebo Directorem nebude podmínkou fungování.

## 4. Návrh hlavní obrazovky

Jedna stabilní pracovní plocha po vzoru Topview. Přepnutí knihovny nemění rozestavění scény ani časovou osu.

```text
Previs · Kavárna / Záběr 01        [16:9] [24 fps] [Podklady pro AI]
┌────────┬────────────────┬───────────────────────────┬─────────────────┐
│ Scéna  │ KNIHOVNA       │                           │ NÁHLED KAMERY   │
│ Lidé   │ hledat         │         3D PROSTOR        │ [A · Celek ▾]   │
│ Zvířata│ kategorie      │                           │ [obraz kamery]  │
│ Objekty│ náhledy modelů │ lidé · pes · stůl · auto  │ Pohled kamerou  │
│ Prostř.│ nebo pohybů    │ kamery · dráhy            ├─────────────────┤
│ Kamery │                │                           │ VLASTNOSTI      │
│ Poh. ka│ Import mocapu  │ výběr · posun · otočení    │ podle výběru    │
│ Pohyby │                │                           │ model/klip/kam. │
├────────┴────────────────┴───────────────────────────┴─────────────────┤
│ ▶  00:02:12  ·  snímek 60    Přichytávání  ·  Auto klíč  ·  rozsah    │
│ Kamera A    [Dolly in ─────────────────────]                         │
│ Jana        [Chůze ──────────][Zastavení][Rozhovor ─────]             │
│ Pes         [Běh ──────][Stání ────────────────────────]              │
└─────────────────────────────────────────────────────────────────────┘
```

Rozměry panelů se mění tažením a panely lze sbalit. Na běžném pracovním monitoru má největší prostor 3D pohled; kamera jde zvětšit do hlavního pohledu. Tmavý neutrální vzhled, krátké názvy, zřetelně vybraný objekt a omezený počet hlavních akcí. Uživatelské rozhraní nesmí být závislé na barvě jako jediném označení.

### Vizuální směr výsledné aplikace

Petr, 24. 10. 2026: „Mockup pěknej. Jen výsledný vzhled programu musí být blíž Topview — hezký moderní tool.“ Rozvržení a postup z prvního návrhu jsou podklad; vzhled realizované aplikace se má výrazně přiblížit prohlédnutému Topview 3D Builderu.

- Výchozí tmavé studiové prostředí: téměř černá pracovní plocha, neutrální antracitové panely a klidná mřížka. Barvy scény nesmějí tónovat celé rozhraní.
- Kompaktní horní lišta a levá navigace s jednoduchými ikonami a krátkými názvy. Většinu okna zabírá obraz, nikoli branding nebo velké ovládací prvky.
- Knihovna se dvěma sloupci výrazných obrazových náhledů modelů a pohybů. Náhled má ukazovat objekt nebo akci, nikoli jen obecnou ikonu kategorie.
- Jemné oddělení panelů, mírně zaoblené náhledy a vstupy, čitelná střídmá typografie a sjednocené rozestupy. Hlavní akce je světlá a výrazná, ostatní prvky zůstávají klidné.
- Akcent slouží výběru, aktivnímu nástroji a stavu ovládání. Preferovaný směr je decentní fialová blízká předloze; přesný odstín je součástí dalšího doladění.
- Kamera zůstává připnutá nad inspektorem, časová osa má čistě oddělené stopy a klipy. Každý panel musí působit jako součást jednoho profesionálního nástroje.

Tento směr se vztahuje na hlavní okno, knihovny, import mocapu, export podkladů i vedlejší dialogy. Finální grafika v Godotu má být vlastní návrh podle této předlohy, nikoli výchozí vzhled editoru enginu. Proklikávací ukázka nadále představuje návrh UI; připravené modely a skutečný renderer vzniknou v technickém prototypu.

### Levé knihovny

- **Scéna**: strom objektů, výběr, přejmenování, skupiny, skrýt, zamknout.
- **Lidé**: připravené rigované figuríny s různými proporcemi, vlastní barvou a jménem.
- **Zvířata**: připravené rigované druhy; u modelu seznam jeho použitelných pohybů.
- **Objekty**: stoly, židle, auta, drobné rekvizity a základní tělesa.
- **Prostředí**: stěny, podlahy, dveře, schody, silnice, budovy, vegetace a hotové sestavy.
- **Kamery**: předvolby postavení a velikosti záběru vůči vybranému aktérovi.
- **Pohyby kamer**: statický záběr, švenk, tilt, dolly, truck, jeřáb, oblet, z ruky, dron.
- **Pohyby**: mocap a další hotové klipy kompatibilní s vybraným modelem; tlačítko **Import mocapu**.

Karta má srozumitelný název a vizuální náhled. U pohybu navíc délku, zdroj a licenční označení; podrobnosti patří do detailu. Náhled pohybu nepřepisuje akci ve scéně. Vložení proběhne na aktuální snímek nebo přetažením do stopy.

### Kontextový pravý panel

Nad panelem zůstává náhled kamery. Kliknutí na model zobrazí polohu, směr, rozměr a barvu; na klip jeho začátek, ořez, rychlost, smyčku a režim posunu; na kameru ohnisko, cíl, sledování a pohyb. Uživatel vždy vidí, který objekt či klip upravuje.

## 5. Mocap: knihovna a import jsou základ

### Dodávaná knihovna

Počítáme se zdroji z dodané studie a Petrova seznamu: **CMU, Eyes, JAPAN, 100STYLE, ACCAD, HDM05 a CC0 balíčky včetně Quaternius**. Oddělené NC kolekce: **Bandai Namco, InterAct a Talking With Hands**.

Pro uživatele budou sjednocené do filmových kategorií: chůze a běh, stání, rozhovor, gesta a emoce, sezení a vstávání, práce s rekvizitou, akce a pády, dvojice. Filtry podle stylu, zdroje, délky a kompatibility. Vyhledávání pod českými i původními názvy.

Program má počítat se stovkami až tisíci klipů v katalogu, ale instalovat je po balíčcích. Základní instalace obsahuje kurátorský výběr, další kolekce si uživatel přidá ve správci knihoven. Stažené balíčky fungují offline. Před samotnou implementací zatím nebyl žádný balík stažen ani připraven.

Klipy nesou autora, zdrojový identifikátor, licenci, případné SA/NC označení a verzi převodu. Volba **Jen komerčně použitelné** filtruje katalog; manifest stejná označení zachová i v přehledu použitých assetů. Obsahová pipeline před přibalením zkontroluje konkrétní podmínky vybraných souborů podle původního licenčního výzkumu.

Párové záznamy se ukládají jako spojená akce dvou aktérů se společným časovým počátkem a relativním rozmístěním. Jejich souřadnice se při importu nesmějí nezávisle vynulovat: zničilo by to kontakt dvojice.

### Import vlastního pohybu

**BVH, FBX a GLB/glTF s animacemi patří do prvního použitelného vydání.** Import má přidat pohyb do knihovny a přiřadit ho existujícímu předrigovanému modelu. Mesh ze zdrojového FBX není nutné vložit do scény.

Průvodce: **soubor → výběr klipu → cílový model → náhled převodu → uložit pohyb**. Známou kostru rozpozná připravený profil. U neznámé kostry nabídne jednorázové mapování hlavních kostí, které se uloží pro další soubory ze stejného zdroje. Oprava měřítka, os a výšky nad zemí je součástí importu.

To, že má model rig, samo nezaručuje shodu se všemi zdrojovými kostrami. Převod musí mít vizuální náhled; nepodporovaná kostra se označí konkrétně. Uživatel kvůli tomu netvoří rig ani animaci. Godot poskytuje běhové načítání FBX; BVH parser, profily a převod pohybů jsou naše práce. [Běhový import](https://docs.godotengine.org/en/stable/tutorials/io/runtime_file_loading_and_saving.html).

### Použití klipu v záběru

Posunout, oříznout, vybrat začátek zdrojového klipu, změnit rychlost, opakovat, zrcadlit a prolnout sousední pohyby. Dva výslovné režimy:

- **Posun z mocapu**: trajektorii přináší samotný klip.
- **Po nakreslené dráze**: dopředný posun řídí dráha, klip poskytuje pohyb těla. Původní dopředný posun se potlačí.

Režimy se nesčítají. Výchozí rychlost dráhy se odvodí od přirozené rychlosti vybraného pohybu; změna tempa upraví časování tak, aby se omezilo klouzání nohou.

## 6. Zvířata a stavba světa

Zvíře je plnohodnotný aktér s vlastní stopou, dráhou a knihovnou pohybů. Pro první technickou zkoušku pes; pro první obsahové vydání navrhuji psa a koně. Další druhy jako samostatné připravené balíčky.

Každý druh má vlastní profil kostry a ověřené kompatibilní klipy. Lidský mocap se automaticky nenabízí psovi a pohyb psa se automaticky nepovažuje za použitelný pro koně. Zvířecí import využije stejný průvodce s odpovídajícím profilem. Výběr knihoven z výše uvedené studie řeší hlavně lidi; zvířecí obsah je třeba připravit samostatně.

Stavba prostředí: přetažení objektu, náhled umístění, přichycení na zem či povrch, otočení, rozměry v metrech, duplikování, zarovnání, skupiny a uzamčení. Hotové sestavy typu **stůl se čtyřmi židlemi**, **kancelář**, **úsek ulice** lze upravit jako skupinu a rozdělit na jednotlivé objekty.

Místnost vznikne z podlahy, stěn, otvorů dveří a oken. Cílem je rychle popsat prostor a překážky ve filmovém záběru. Auto může být statická nebo po dráze pohybující se rekvizita. Automatická dopravní simulace a fyzika vozidla nejsou potřebné pro první verzi.

Pro sezení má židle kontaktní bod a směr. Připravená sedací akce se k němu zarovná s možností upravit celkový posun. Složité kontaktní akce mezi libovolnými modely se nebudou odvozovat jen ze samotné existence mocap klipu.

## 7. Kamery a časování

Více kamer nad stejnou akcí: celek, detail, protizáběr, sledování. Kamera je viditelný objekt s jehlanem a má samostatný živý náhled.

- předvolby podle velikosti a polohy aktéra: celek, polocelek, detail, přes rameno, bok, nadhled, podhled;
- ohnisko v mm, velikost snímače a poměr stran; zobrazení kompozičních vodítek;
- Look-at na objekt nebo bod, Follow s nastavenou vzdáleností;
- pohyby jako klipy na stopě, délka, intenzita a náběh/dojezd;
- pilotování kamery myší a WASD/QE, zápis klíče na aktuálním snímku nebo automaticky;
- samostatné kanály polohy, orientace, cíle a ohniska, zobrazené až po rozbalení.

Předvolba pohybu a ruční klíče nesmějí bez vysvětlení řídit tentýž kanál současně. Převod předvolby na upravitelné klíče zachová viditelný výsledek. Look-at řídí orientaci pouze při výslovném zapnutí; panel ukáže aktivní způsob řízení.

Každá kamera může zobrazovat referenční obrázek nebo video. Video stojí přesně na čase aktuálního snímku scény, včetně přepočtu jiné snímkové frekvence a proměnlivých časových značek. Krytí a režim před/za scénou usnadní srovnání kompozice. Referenční obraz nepatří do hloubky, hran, masek ani pózy.

Ovládání prostoru: oblet, posun, zoom a zaměření na výběr; létání jen v aktivovaném režimu, aby WASD nekolidovalo s psaním a transformačními zkratkami. Kompletní undo/redo zahrnuje umísťování objektů, import do projektu, klipy, dráhy i kamery.

## 8. Výstupy pro generování AI videa

Hlavní tlačítko **Podklady pro AI** otevře náhled a volby kamery, rozsahu, fps, poměru stran, rozlišení a druhů výstupu. Výchozí jednoduchý export: stínované vodicí video a první snímek. Další průchody uživatel zapne podle cílového workflow.

| Výstup | Účel |
|---|---|
| Šedý stínovaný previz | Pohyb, prostor, kompozice a kamera. |
| Barevně odlišení aktéři | Identita a přiřazení rolí; jména a barvy jsou i v metadatech. |
| Hloubka | Hloubkové řízení, stejný převod vzdálenosti na hodnotu přes celý záběr. |
| Hrany | Geometrie, siluety a pohyb obrysů. |
| Masky aktérů/objektů | Samostatné oblasti pro navazující zpracování. |
| Lidská póza | Export podle výslovně podporovaného formátu kostry. |
| První a volitelně poslední snímek | Podklad pro obrazový vzhled a řízení krajních stavů záběru. |
| JSON + čitelný přehled záběru | Aktéři, barvy, pohyby s časy, kamera, ohnisko, dráhy, fps a rozsah. |

U zvířat jsou hlavní šedý/barevný obraz, hloubka, hrany a masky. Zvířecí kostru nelze označovat jako automaticky kompatibilní lidský OpenPose. Obecná skeleton data lze vyvézt zvlášť s názvem profilu.

MP4 pro pohodlné použití a snímkové sekvence pro přesné návazné zpracování. U hloubky navíc bezeztrátový výstup s popisem jednotek a převodu. Cíl až UHD/4K; rychlost ani kapacita zatím nejsou změřené. Export se vyhodnocuje snímek po snímku a nemusí běžet v reálném čase.

Exportní balíček obsahuje jen zvolené výstupy, náhled a metadata. Editorové štítky, mřížka, táhla a ikony kamer jsou z průchodů vyloučené. Pozadí lze na přání přidat pouze do kontrolního barevného videa. Jednotlivé průchody se nevkládají všechny automaticky do každého AI modelu.

První verze podklady ukládá lokálně. Přímé odesílání do generátorů nebo Directoru lze doplnit samostatnými adaptéry. Základní program není závislý na konkrétním AI poskytovateli.

## 9. Data, přenositelnost a přesnost

Projekt uchovává prostředí, aktéry, knihovny, akce a pojmenované záběry. **Akce** je společný průběh pohybů; **záběr** vybírá kameru a úsek akce. Pro jiné herecké provedení vznikne pojmenovaná kopie akce, takže se nezmění původní pokrytí. V UI stačí přehled záběrů, nikoli další střihová aplikace.

Projektový formát bude verzovaný dokument s relativními odkazy a stabilními ID assetů. Pracovní adresář umožní rychlé ukládání; **Zabalit projekt** přidá použité modely, klipy a média do přenosného balíčku. Mezipaměti a proxy jsou obnovitelné. Chybějící asset nabídne opětovné nalezení podle identifikátoru. Automatické zálohy chrání rozpracovanou scénu.

Časovou autoritou je číslo snímku a přesná snímková frekvence. Jedna společná funkce vyhodnotí celý stav pro daný snímek: klipy, prolnutí, dráhy, kořenový posun, kamery a pozadí. Shodný snímek musí dát stejný výsledek po přímém skoku, přehrávání i exportu. Chvění kamery využije uložený seed. Náhodné nebo fyzikální efekty se před použitím musí vyhodnocovat opakovatelně nebo předpočítat. `AnimationPlayer.seek()` je jen dílčí stavební kámen této přesnosti. [AnimationPlayer](https://docs.godotengine.org/en/stable/classes/class_animationplayer.html).

Kód rozdělíme na C# jádro dokumentu a času, runtime scénu Godotu, UI, import/katalog a exportní worker. Běžná logika dokumentu zůstane oddělená od zobrazení. Exportní worker bude používat stejné vyhodnocení jako náhled.

Windows je hlavní vývojová platforma. Od prvního technického průřezu budou potřeba skutečné balíčky a zkoušky na macOS a Linuxu: import, ovládání, fonty/DPI, video pozadí, otevření stejného projektu a export. Multiplatformní engine sám tyto zkoušky nenahrazuje.

## 10. Navržené etapy

| Etapa | Konkrétní výsledek |
|---|---|
| **A — ověření základu** | Vlastní jednoduché UI, připravený člověk a pes, skutečný BVH/FBX import na člověka, kompatibilní zvířecí pohyb, dráha, dvě kamery, přesný skok na snímek, video pozadí a první hloubka/maska. Zkouška všech tří OS. |
| **B — první použitelná verze** | Celý postup od stavby scény přes mocap a kameru po AI balíček; kurátorská knihovna lidí, zvířat a rekvizit; ukládání/přenos projektu, undo, náhledy, více záběrů. |
| **C — rozšíření obsahu** | Stovky připravených klipů, správce velkých kolekcí, NC kolekce, další druhy zvířat, sestavy prostředí a párové akce. |
| **D — návazné workflow** | Ověřený export scén/kamer do Blenderu a Nuke, profily podkladů pro konkrétní AI workflow, případně napojení na Director. Scéna z textu/obrázku až jako další samostatná funkce. |

Import mocapu a export vodicích podkladů jsou již součástí B, ne dodatečné rozšíření. Rozsah první verze se měří dokončeným záběrem, ne počtem obsloužených rigů nebo tlačítek.

**Přejímací scéna:** dvorek kavárny, dvě osoby, pes, stůl, židle a auto. Importovaný pohyb převede osobu ke stolu; pes proběhne po dráze a zastaví. Tři záběry: celek, sledování a detail. Export 5–10 sekund se šedým obrazem, hloubkou a maskami, první snímek a přehled časování. Projekt zabalit a otevřít na druhém OS. U referenčního videa s vypálenými čísly snímků ověřit 25fps zdroj pod 24fps scénou. Následně podklady skutečně použít v jednom zvoleném AI video workflow; jeho výsledek posoudit zvlášť od přesnosti Previsu.

## 11. Co tento návrh zatím neprohlašuje za hotové

Funkční prototyp 0.2.1 má vlastní scénu Godotu, připravenou figurínu, BVH klipové stopy s prolnutím, kreslené dráhy, základní rekvizity, více samostatných kamer a neutrální pracovní osvětlení. Proklikávací návrh z předchozí fáze nadále slouží jako vizuální reference. Kompletní distribuovatelné knihovny, animovaná zvířata, filmový export všech průchodů a ověření ostatních OS zůstávají další prací. Stav konkrétního sestavení je v `README.md` a `VALIDATION.md`.
