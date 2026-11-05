# SkiaBgRepro – reprodukcija EXC_BAD_ACCESS ob prehodu v ozadje

Zahteve: Mac + Xcode, `dotnet workload install maui`, **fizični iPhone**, Release (`-c Release`) ali Debug.

## Koraki
1. Zaženi na napravi. Rdeč napis = način UNSAFE.
2. 8 canvasov se osvežuje ~250x/s vsak (bitmape se zamenjujejo in sproščajo v ozadju).
3. Pritiskaj Home / povleci navzgor (App Switcher) / zakleni telefon, vrni se, ponavljaj 10-30x.
4. Pričakovano: občasen SIGSEGV / EXC_BAD_ACCESS (nativni crash, `caught` se ne poveča).
   Po restartu zgoraj piše, po katerem lifecycle dogodku se je prejšnja seja končala.
5. Pritisni "Preklopi" -> SAFE (zelen). Ponovi isti test: crash naj izgine
   (lifecycle gating + dispose pod lockom + InvalidateSurface na UI niti z Handler preverjanjem).

Hitrejša varianta na simulatorju: gumb "Stres" vsakih 60 ms uniči in ponovno ustvari
vse SKCanvasView, medtem ko časovniki še tečejo - to simulira teardown native površine.

## Opombe
- Gre za race condition, zato je crash verjeten, ne zagotovljen. Če ga ne vidiš, povečaj
  CameraCount ali zmanjšaj interval v CameraSim.Start().
- Za simbolizirano sled shrani dSYM (Archive) in odpri crash v Xcode > Devices > View Device Logs.

## Scenarij: kamera ne odgovori
Neodvisni gumbi (neodvisni od Unsafe/Safe):
- "Kamera HTTP": 8 zank, ki posnemajo tvojo kodo (TcpClient.ConnectAsync brez using/timeouta, nato HttpClient.GetAsync, timeout 20 s).
- "catch (Exception)": izklopljeno = catch samo za HttpRequestException (kot pogosta napaka); vklopljeno = ujame vse.
- "TCP pre-check": vklopljeno privzeto; izklopi, da vidiš samo HttpClient.

A) catch IZKLOPLJEN: Kamera HTTP, Home, počakaj >20 s (TCP connect lahko traja še dlje), vrni se -> neujeta SocketException ali TaskCanceledException -> CRASH.
B) catch VKLOPLJEN: isti test -> aplikacija deluje, httpHandled raste.
Po crashu zgoraj piše "Zadnja izjema" (tip izjeme).
Namig: za takojšnjo SocketException zamenjaj CameraHost za dosegljiv naslov z zaprtim portom.
