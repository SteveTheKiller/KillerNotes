// Note and group workflows, shared by the landing, help and technical pages.
(function () {
  var updates = {
  "it": [
    "Fai clic destro su una nota per aprire la sua scheda: fissaggio, condivisione, esportazione, cronologia, colore, tag e gruppo. Duplica (<code>Alt+Shift+U</code>) crea una copia; Salva come modello (<code>Alt+Shift+T</code>) la copia nel gruppo dei modelli. Seleziona più note per modificarne insieme fissaggio, colore, tag o gruppo.",
    "Organizza nella barra laterale (<code>Alt+Shift+O</code>) apre un albero dei gruppi ricercabile e un elenco di note ordinabile. Trascina le note nei gruppi o i gruppi nei gruppi padre; il clic destro apre le stesse schede della barra laterale. La scheda del gruppo offre nome, colore, gruppo padre e ruoli modello e giornaliero. <code>Alt+Shift+H</code> crea una nota nel gruppo selezionato; <code>Alt+Shift+Z</code> comprime tutti i gruppi. <code>Ctrl+Z</code> annulla le modifiche organizzative.",
    "La freccia accanto a Nuova nota apre le opzioni per note Markdown, modelli, nota di oggi, nota in un gruppo scelto e importazione di cartelle (<code>Alt+Shift+I</code>). L'esportazione Markdown dell'intero blocco note si trova nel pulsante della raccolta sulla barra laterale.",
    "Contrassegna un gruppo come gruppo dei modelli nella sua scheda aperta con il clic destro. Le sue note compaiono nel menu Nuova nota sotto Nuova nota da modello, accessibile anche con <code>Alt+T</code>. Spostare una nota dentro o fuori dal gruppo cambia il suo ruolo di modello.",
    "Scegli Nuova nota Markdown nel menu Nuova nota (<code>Alt+N</code>). Il controllo Testo formattato / Markdown nella scheda della nota aperta con il clic destro converte una nota alla volta (<code>Alt+F</code>), avvisando prima di una conversione con perdite.",
    "Usa Esporta tutte le note come Markdown nel pulsante della raccolta sulla barra laterale per scrivere l'intero blocco note in file <code>.md</code>. I gruppi diventano sottocartelle; i metadati iniziali contengono titoli, tag e date."
  ],
  "es": [
    "Haz clic derecho en una nota para abrir su tarjeta: fijar, compartir, exportar, historial, color, etiquetas y grupo. Duplicar (<code>Alt+Shift+U</code>) crea una copia; Guardar como plantilla (<code>Alt+Shift+T</code>) la copia al grupo de plantillas. Selecciona varias notas para cambiar juntas su fijación, color, etiquetas o grupo.",
    "Organizar en la barra lateral (<code>Alt+Shift+O</code>) abre un árbol de grupos con búsqueda y una lista de notas ordenable. Arrastra notas a grupos o grupos a sus grupos superiores; el clic derecho abre las mismas tarjetas que la barra lateral. La tarjeta del grupo permite cambiar nombre, color, grupo superior y funciones de plantilla y notas diarias. <code>Alt+Shift+H</code> crea una nota en el grupo seleccionado; <code>Alt+Shift+Z</code> contrae todos los grupos. <code>Ctrl+Z</code> deshace los cambios de organización.",
    "La flecha junto a Nueva nota abre las opciones de notas Markdown, plantillas, nota de hoy, nota en un grupo elegido e importación de carpetas (<code>Alt+Shift+I</code>). La exportación Markdown de todo el bloc está en el botón de biblioteca de la barra lateral.",
    "Marca un grupo como grupo de plantillas en su tarjeta de clic derecho. Sus notas aparecen en el menú Nueva nota, bajo Nueva nota desde plantilla, también accesible con <code>Alt+T</code>. Mover una nota dentro o fuera de ese grupo determina si es una plantilla.",
    "Elige Nueva nota Markdown en el menú Nueva nota (<code>Alt+N</code>). El control Texto enriquecido / Markdown de la tarjeta de clic derecho convierte una nota a la vez (<code>Alt+F</code>), con un aviso antes de una conversión con pérdidas.",
    "Usa Exportar todas las notas como Markdown en el botón de biblioteca de la barra lateral para escribir todo el bloc como archivos <code>.md</code>. Los grupos se convierten en subcarpetas; los metadatos iniciales incluyen títulos, etiquetas y fechas."
  ],
  "de": [
    "Ein Rechtsklick auf eine Notiz öffnet ihre Karte: Anheften, Teilen, Export, Verlauf, Farbe, Tags und Gruppe. Duplizieren (<code>Alt+Shift+U</code>) erstellt eine Kopie; Als Vorlage speichern (<code>Alt+Shift+T</code>) kopiert sie in die Vorlagengruppe. Wähle mehrere Notizen, um Anheftung, Farbe, Tags oder Gruppe gemeinsam zu ändern.",
    "Organisieren in der Seitenleiste (<code>Alt+Shift+O</code>) öffnet einen durchsuchbaren Gruppenbaum und eine sortierbare Notizliste. Ziehe Notizen in Gruppen oder Gruppen in übergeordnete Gruppen; ein Rechtsklick öffnet dieselben Karten wie in der Seitenleiste. Die Gruppenkarte bietet Name, Farbe, übergeordnete Gruppe sowie Vorlagen- und Tagesnotizrolle. <code>Alt+Shift+H</code> erstellt eine Notiz in der ausgewählten Gruppe; <code>Alt+Shift+Z</code> klappt alle Gruppen ein. <code>Ctrl+Z</code> macht Änderungen an der Organisation rückgängig.",
    "Der Pfeil neben Neue Notiz öffnet die Optionen für Markdown-Notizen, Vorlagen, die heutige Notiz, eine Notiz in einer gewählten Gruppe und den Ordnerimport (<code>Alt+Shift+I</code>). Der Markdown-Export des gesamten Notizbuchs liegt auf der Bibliotheksschaltfläche der Seitenleiste.",
    "Markiere eine Gruppe auf ihrer Rechtsklickkarte als Vorlagengruppe. Ihre Notizen erscheinen im Menü Neue Notiz unter Neue Notiz aus Vorlage; auch <code>Alt+T</code> öffnet die Auswahl. Eine Notiz wird durch Verschieben in diese Gruppe zur Vorlage und durch Herausnehmen wieder zur normalen Notiz.",
    "Wähle Neue Markdown-Notiz im Menü Neue Notiz (<code>Alt+N</code>). Der Schalter Rich Text / Markdown auf der Rechtsklickkarte wandelt jeweils eine Notiz um (<code>Alt+F</code>) und warnt vor einer verlustbehafteten Umwandlung.",
    "Mit Alle Notizen als Markdown exportieren auf der Bibliotheksschaltfläche der Seitenleiste schreibst du das gesamte Notizbuch als <code>.md</code>-Dateien. Gruppen werden zu Unterordnern; die Metadaten am Dateianfang enthalten Titel, Tags und Datumsangaben."
  ],
  "fr": [
    "Un clic droit sur une note ouvre sa fiche : épinglage, partage, exportation, historique, couleur, étiquettes et groupe. Dupliquer (<code>Alt+Shift+U</code>) crée une copie ; Enregistrer comme modèle (<code>Alt+Shift+T</code>) la copie dans le groupe des modèles. Sélectionnez plusieurs notes pour modifier ensemble leur épinglage, couleur, étiquettes ou groupe.",
    "Organiser dans la barre latérale (<code>Alt+Shift+O</code>) ouvre une arborescence de groupes avec recherche et une liste de notes triable. Glissez les notes dans les groupes ou les groupes dans leurs parents ; le clic droit ouvre les mêmes fiches que la barre latérale. La fiche du groupe propose nom, couleur, parent et rôles de modèle et de notes quotidiennes. <code>Alt+Shift+H</code> crée une note dans le groupe sélectionné ; <code>Alt+Shift+Z</code> replie tous les groupes. <code>Ctrl+Z</code> annule les changements d'organisation.",
    "La flèche à côté de Nouvelle note ouvre les options pour les notes Markdown, les modèles, la note du jour, une note dans un groupe choisi et l'importation de dossiers (<code>Alt+Shift+I</code>). L'exportation Markdown du bloc-notes entier se trouve sur le bouton de bibliothèque de la barre latérale.",
    "Marquez un groupe comme groupe de modèles dans sa fiche accessible par clic droit. Ses notes apparaissent dans le menu Nouvelle note, sous Nouvelle note à partir d'un modèle, également accessible avec <code>Alt+T</code>. Déplacer une note dans ce groupe ou l'en sortir détermine si elle sert de modèle.",
    "Choisissez Nouvelle note Markdown dans le menu Nouvelle note (<code>Alt+N</code>). Le contrôle Texte enrichi / Markdown de la fiche accessible par clic droit convertit une note à la fois (<code>Alt+F</code>), avec un avertissement avant toute conversion avec pertes.",
    "Utilisez Exporter toutes les notes en Markdown sur le bouton de bibliothèque de la barre latérale pour écrire le bloc-notes entier dans des fichiers <code>.md</code>. Les groupes deviennent des sous-dossiers ; les métadonnées initiales contiennent titres, étiquettes et dates."
  ],
  "pt": [
    "Clique com o botão direito em uma nota para abrir seu cartão: fixar, compartilhar, exportar, histórico, cor, etiquetas e grupo. Duplicar (<code>Alt+Shift+U</code>) cria uma cópia; Salvar como modelo (<code>Alt+Shift+T</code>) a copia para o grupo de modelos. Selecione várias notas para alterar juntas a fixação, a cor, as etiquetas ou o grupo.",
    "Organizar na barra lateral (<code>Alt+Shift+O</code>) abre uma árvore de grupos com busca e uma lista de notas ordenável. Arraste notas para grupos ou grupos para grupos superiores; o clique direito abre os mesmos cartões da barra lateral. O cartão do grupo permite alterar nome, cor, grupo superior e funções de modelo e notas diárias. <code>Alt+Shift+H</code> cria uma nota no grupo selecionado; <code>Alt+Shift+Z</code> recolhe todos os grupos. <code>Ctrl+Z</code> desfaz alterações na organização.",
    "A seta ao lado de Nova nota abre as opções de notas Markdown, modelos, nota de hoje, nota em um grupo escolhido e importação de pastas (<code>Alt+Shift+I</code>). A exportação Markdown do bloco inteiro fica no botão da biblioteca na barra lateral.",
    "Marque um grupo como grupo de modelos no cartão aberto com o botão direito. Suas notas aparecem no menu Nova nota, em Nova nota a partir de modelo, também acessível com <code>Alt+T</code>. Mover uma nota para dentro ou para fora desse grupo determina se ela é um modelo.",
    "Escolha Nova nota Markdown no menu Nova nota (<code>Alt+N</code>). O controle Texto formatado / Markdown no cartão aberto com o botão direito converte uma nota por vez (<code>Alt+F</code>), com um aviso antes de uma conversão com perdas.",
    "Use Exportar todas as notas como Markdown no botão da biblioteca na barra lateral para gravar o bloco inteiro em arquivos <code>.md</code>. Os grupos viram subpastas; os metadados iniciais contêm títulos, etiquetas e datas."
  ],
  "nb": [
    "Høyreklikk en note for å åpne kortet: festing, deling, eksport, historikk, farge, etiketter og gruppe. Dupliser (<code>Alt+Shift+U</code>) lager en kopi; Lagre som mal (<code>Alt+Shift+T</code>) kopierer den til malgruppen. Velg flere noter for å endre festing, farge, etiketter eller gruppe samlet.",
    "Organiser på sidelinjen (<code>Alt+Shift+O</code>) åpner et søkbart gruppetre og en sorterbar noteliste. Dra noter inn i grupper eller grupper inn i overordnede grupper; høyreklikk åpner de samme kortene som i sidelinjen. Gruppekortet tilbyr navn, farge, overordnet gruppe og roller for maler og dagsnoter. <code>Alt+Shift+H</code> lager en note i valgt gruppe; <code>Alt+Shift+Z</code> slår sammen alle grupper. <code>Ctrl+Z</code> angrer endringer i organiseringen.",
    "Pilen ved siden av Ny note åpner valgene for Markdown-noter, maler, dagens note, en note i en valgt gruppe og mappeimport (<code>Alt+Shift+I</code>). Markdown-eksport av hele notisboken ligger på bibliotekknappen i sidelinjen.",
    "Merk en gruppe som malgruppe på høyreklikkortet. Notene i gruppen vises i Ny note-menyen under Ny note fra mal, eller trykk <code>Alt+T</code>. Når en note flyttes inn i eller ut av gruppen, endres det om den er en mal.",
    "Velg Ny Markdown-note i Ny note-menyen (<code>Alt+N</code>). Rik tekst / Markdown-kontrollen på notens høyreklikkort konverterer én note om gangen (<code>Alt+F</code>), med en advarsel før konvertering med tap.",
    "Bruk Eksporter alle noter som Markdown på bibliotekknappen i sidelinjen for å skrive hele notisboken til <code>.md</code>-filer. Grupper blir undermapper; metadataene først i filen inneholder titler, etiketter og datoer."
  ],
  "sl": [
    "Z desnim klikom na zapisek odprete njegovo kartico: pripenjanje, deljenje, izvoz, zgodovina, barva, oznake in skupina. Podvoji (<code>Alt+Shift+U</code>) ustvari kopijo; Shrani kot predlogo (<code>Alt+Shift+T</code>) jo kopira v skupino predlog. Izberite več zapiskov, da jim skupaj spremenite pripetost, barvo, oznake ali skupino.",
    "Organiziraj v stranski vrstici (<code>Alt+Shift+O</code>) odpre drevo skupin z iskanjem in razvrstljiv seznam zapiskov. Povlecite zapiske v skupine ali skupine v nadrejene skupine; desni klik odpre iste kartice kot stranska vrstica. Kartica skupine ponuja ime, barvo, nadrejeno skupino ter vlogi za predloge in dnevne zapiske. <code>Alt+Shift+H</code> ustvari zapisek v izbrani skupini; <code>Alt+Shift+Z</code> strne vse skupine. <code>Ctrl+Z</code> razveljavi spremembe razporeditve.",
    "Puščica ob Nov zapisek odpre možnosti za zapiske Markdown, predloge, današnji zapisek, zapisek v izbrani skupini in uvoz mape (<code>Alt+Shift+I</code>). Izvoz celotnega zvezka v Markdown je na gumbu knjižnice v stranski vrstici.",
    "Na kartici skupine, ki jo odprete z desnim klikom, jo označite kot skupino predlog. Njeni zapiski se pojavijo v meniju Nov zapisek pod Nov zapisek iz predloge, ali pritisnite <code>Alt+T</code>. Premik zapiska v to skupino ali iz nje določa, ali je predloga.",
    "V meniju Nov zapisek izberite Nov zapisek Markdown (<code>Alt+N</code>). Stikalo Obogateno besedilo / Markdown na kartici zapiska z desnim klikom pretvori po en zapisek (<code>Alt+F</code>) in pred pretvorbo z izgubami opozori.",
    "Na gumbu knjižnice v stranski vrstici uporabite Izvozi vse zapiske kot Markdown, da celoten zvezek zapišete v datoteke <code>.md</code>. Skupine postanejo podmape; začetni metapodatki vsebujejo naslove, oznake in datume."
  ],
  "pl": [
    "Kliknij notatkę prawym przyciskiem, aby otworzyć jej kartę: przypinanie, udostępnianie, eksport, historia, kolor, tagi i grupa. Duplikuj (<code>Alt+Shift+U</code>) tworzy kopię; Zapisz jako szablon (<code>Alt+Shift+T</code>) kopiuje ją do grupy szablonów. Zaznacz kilka notatek, aby wspólnie zmienić przypięcie, kolor, tagi lub grupę.",
    "Organizuj na bocznym pasku (<code>Alt+Shift+O</code>) otwiera drzewo grup z wyszukiwaniem i sortowalną listę notatek. Przeciągaj notatki do grup lub grupy do grup nadrzędnych; prawy przycisk otwiera te same karty co pasek boczny. Karta grupy pozwala zmienić nazwę, kolor, grupę nadrzędną oraz role szablonów i notatek dziennych. <code>Alt+Shift+H</code> tworzy notatkę w wybranej grupie; <code>Alt+Shift+Z</code> zwija wszystkie grupy. <code>Ctrl+Z</code> cofa zmiany organizacji.",
    "Strzałka obok Nowa notatka otwiera opcje notatek Markdown, szablonów, dzisiejszej notatki, notatki w wybranej grupie i importu folderu (<code>Alt+Shift+I</code>). Eksport całego notatnika do Markdown znajduje się pod przyciskiem biblioteki na bocznym pasku.",
    "Oznacz grupę jako grupę szablonów na jej karcie otwieranej prawym przyciskiem. Jej notatki pojawią się w menu Nowa notatka pod Nowa notatka z szablonu; możesz też nacisnąć <code>Alt+T</code>. Przeniesienie notatki do tej grupy lub poza nią określa, czy jest szablonem.",
    "Wybierz Nowa notatka Markdown w menu Nowa notatka (<code>Alt+N</code>). Przełącznik Tekst sformatowany / Markdown na karcie notatki otwieranej prawym przyciskiem konwertuje jedną notatkę naraz (<code>Alt+F</code>), ostrzegając przed konwersją ze stratami.",
    "Użyj Eksportuj wszystkie notatki jako Markdown pod przyciskiem biblioteki na bocznym pasku, aby zapisać cały notatnik jako pliki <code>.md</code>. Grupy stają się podfolderami; metadane na początku zawierają tytuły, tagi i daty."
  ],
  "cs": [
    "Kliknutím pravým tlačítkem na poznámku otevřete její kartu: připnutí, sdílení, export, historii, barvu, štítky a skupinu. Duplikovat (<code>Alt+Shift+U</code>) vytvoří kopii; Uložit jako šablonu (<code>Alt+Shift+T</code>) ji zkopíruje do skupiny šablon. Vyberte více poznámek a změňte jejich připnutí, barvu, štítky nebo skupinu společně.",
    "Uspořádat na postranní liště (<code>Alt+Shift+O</code>) otevře prohledávatelný strom skupin a řaditelný seznam poznámek. Přetahujte poznámky do skupin nebo skupiny do nadřazených skupin; pravé tlačítko otevře stejné karty jako postranní panel. Karta skupiny nabízí název, barvu, nadřazenou skupinu a role šablon a denních poznámek. <code>Alt+Shift+H</code> vytvoří poznámku ve vybrané skupině; <code>Alt+Shift+Z</code> sbalí všechny skupiny. <code>Ctrl+Z</code> vrátí změny uspořádání.",
    "Šipka vedle Nová poznámka otevře možnosti poznámek Markdown, šablon, dnešní poznámky, poznámky ve vybrané skupině a importu složky (<code>Alt+Shift+I</code>). Export celého zápisníku do Markdown je pod tlačítkem knihovny na postranní liště.",
    "Na kartě skupiny otevřené pravým tlačítkem ji označte jako skupinu šablon. Její poznámky se objeví v nabídce Nová poznámka pod Nová poznámka ze šablony; můžete také stisknout <code>Alt+T</code>. Přesunutí poznámky do této skupiny nebo z ní určuje, zda je šablonou.",
    "V nabídce Nová poznámka zvolte Nová poznámka Markdown (<code>Alt+N</code>). Přepínač Formátovaný text / Markdown na kartě poznámky otevřené pravým tlačítkem převádí jednu poznámku po druhé (<code>Alt+F</code>) a před ztrátovým převodem varuje.",
    "Pomocí Exportovat všechny poznámky jako Markdown pod tlačítkem knihovny na postranní liště zapíšete celý zápisník do souborů <code>.md</code>. Skupiny se stanou podsložkami; úvodní metadata obsahují názvy, štítky a data."
  ],
  "hu": [
    "Kattints jobb gombbal egy jegyzetre a kártyájához: rögzítés, megosztás, exportálás, előzmények, szín, címkék és csoport. A Másolat készítése (<code>Alt+Shift+U</code>) másolatot hoz létre; a Mentés sablonként (<code>Alt+Shift+T</code>) a sabloncsoportba másolja. Jelölj ki több jegyzetet a rögzítés, szín, címkék vagy csoport együttes módosításához.",
    "Az oldalsáv Rendezés gombja (<code>Alt+Shift+O</code>) kereshető csoportfát és rendezhető jegyzetlistát nyit meg. Húzd a jegyzeteket csoportokba, a csoportokat pedig szülőcsoportokba; jobb kattintással az oldalsávval azonos kártyák nyílnak meg. A csoportkártyán név, szín, szülőcsoport, valamint sablon- és napi szerep állítható. Az <code>Alt+Shift+H</code> jegyzetet készít a kijelölt csoportban; az <code>Alt+Shift+Z</code> minden csoportot összecsuk. A <code>Ctrl+Z</code> visszavonja a rendezési változtatásokat.",
    "Az Új jegyzet melletti nyíl megnyitja a Markdown-jegyzet, sablon, mai jegyzet, választott csoportba kerülő jegyzet és mappaimportálás (<code>Alt+Shift+I</code>) lehetőségeit. A teljes jegyzetfüzet Markdown-exportja az oldalsáv könyvtárgombján található.",
    "Jelölj ki egy csoportot sabloncsoportként a jobb kattintással megnyitott kártyáján. Jegyzetei az Új jegyzet menü Új jegyzet sablonból pontjában jelennek meg, vagy nyomj <code>Alt+T</code>-t. A csoportba helyezés vagy onnan kivétel határozza meg, hogy egy jegyzet sablon-e.",
    "Az Új jegyzet menüben válaszd az Új Markdown-jegyzet lehetőséget (<code>Alt+N</code>). A jegyzet jobb kattintásos kártyáján a Formázott szöveg / Markdown kapcsoló egyszerre egy jegyzetet alakít át (<code>Alt+F</code>), veszteséges átalakítás előtt figyelmeztetéssel.",
    "Az oldalsáv könyvtárgombján a Minden jegyzet exportálása Markdownként lehetőséggel a teljes jegyzetfüzetet <code>.md</code> fájlokba írhatod. A csoportok almappák lesznek; a kezdő metaadatok a címeket, címkéket és dátumokat tartalmazzák."
  ],
  "tr": [
    "Bir notun kartını açmak için sağ tıklayın: sabitleme, paylaşma, dışa aktarma, geçmiş, renk, etiketler ve grup. Çoğalt (<code>Alt+Shift+U</code>) bir kopya oluşturur; Şablon olarak kaydet (<code>Alt+Shift+T</code>) notu şablon grubuna kopyalar. Sabitleme, renk, etiket veya grubu birlikte değiştirmek için birden fazla not seçin.",
    "Yan çubuktaki Düzenle (<code>Alt+Shift+O</code>), aranabilir bir grup ağacı ve sıralanabilir not listesi açar. Notları gruplara, grupları üst gruplara sürükleyin; sağ tıklama yan çubuktaki kartların aynısını açar. Grup kartında ad, renk, üst grup, şablon ve günlük not rolleri bulunur. <code>Alt+Shift+H</code> seçili grupta not oluşturur; <code>Alt+Shift+Z</code> tüm grupları daraltır. <code>Ctrl+Z</code> düzenleme değişikliklerini geri alır.",
    "Yeni not yanındaki ok, Markdown notları, şablonlar, bugünün notu, seçilen grupta not ve klasör içe aktarma (<code>Alt+Shift+I</code>) seçeneklerini açar. Tüm not defterini Markdown olarak dışa aktarma, yan çubuktaki kitaplık düğmesindedir.",
    "Sağ tıklama kartında bir grubu şablon grubu olarak işaretleyin. Notları, Yeni not menüsünde Şablondan yeni not altında görünür; <code>Alt+T</code> de aynı listeyi açar. Bir notu bu gruba taşımak veya gruptan çıkarmak şablon olup olmadığını belirler.",
    "Yeni not menüsünde Yeni Markdown notu seçeneğini kullanın (<code>Alt+N</code>). Notun sağ tıklama kartındaki Zengin metin / Markdown denetimi her seferinde bir notu dönüştürür (<code>Alt+F</code>), kayıplı dönüşüm öncesinde uyarır.",
    "Yan çubuktaki kitaplık düğmesinde Tüm notları Markdown olarak dışa aktar seçeneğiyle tüm not defterini <code>.md</code> dosyalarına yazın. Gruplar alt klasörlere dönüşür; başlangıçtaki meta veriler başlıkları, etiketleri ve tarihleri taşır."
  ],
  "ru": [
    "Щёлкните заметку правой кнопкой, чтобы открыть её карточку: закрепление, общий доступ, экспорт, история, цвет, метки и группа. Дублировать (<code>Alt+Shift+U</code>) создаёт копию; Сохранить как шаблон (<code>Alt+Shift+T</code>) копирует её в группу шаблонов. Выделите несколько заметок, чтобы вместе изменить закрепление, цвет, метки или группу.",
    "Упорядочить на боковой панели (<code>Alt+Shift+O</code>) открывает дерево групп с поиском и сортируемый список заметок. Перетаскивайте заметки в группы, а группы в родительские группы; правая кнопка открывает те же карточки, что и боковая панель. Карточка группы позволяет менять имя, цвет, родителя и назначение для шаблонов и ежедневных заметок. <code>Alt+Shift+H</code> создаёт заметку в выбранной группе; <code>Alt+Shift+Z</code> сворачивает все группы. <code>Ctrl+Z</code> отменяет изменения организации.",
    "Стрелка рядом с Новой заметкой открывает варианты заметок Markdown, шаблонов, сегодняшней заметки, заметки в выбранной группе и импорта папки (<code>Alt+Shift+I</code>). Экспорт всего блокнота в Markdown находится на кнопке библиотеки боковой панели.",
    "Отметьте группу как группу шаблонов в её карточке, открытой правой кнопкой. Её заметки появятся в меню Новая заметка, в пункте Новая заметка из шаблона; можно также нажать <code>Alt+T</code>. Перемещение заметки в эту группу или из неё определяет, является ли она шаблоном.",
    "Выберите Новая заметка Markdown в меню Новая заметка (<code>Alt+N</code>). Переключатель Форматированный текст / Markdown в карточке заметки, открытой правой кнопкой, преобразует по одной заметке (<code>Alt+F</code>), предупреждая перед преобразованием с потерями.",
    "Используйте Экспортировать все заметки как Markdown на кнопке библиотеки боковой панели, чтобы записать весь блокнот в файлы <code>.md</code>. Группы станут подпапками; метаданные в начале содержат заголовки, метки и даты."
  ],
  "uk": [
    "Клацніть нотатку правою кнопкою, щоб відкрити її картку: закріплення, поширення, експорт, історія, колір, мітки та група. Дублювати (<code>Alt+Shift+U</code>) створює копію; Зберегти як шаблон (<code>Alt+Shift+T</code>) копіює її до групи шаблонів. Виберіть кілька нотаток, щоб разом змінити закріплення, колір, мітки або групу.",
    "Упорядкувати на бічній панелі (<code>Alt+Shift+O</code>) відкриває дерево груп із пошуком і список нотаток із сортуванням. Перетягуйте нотатки до груп, а групи до батьківських груп; права кнопка відкриває ті самі картки, що й бічна панель. Картка групи дає змогу змінити назву, колір, батьківську групу та ролі шаблонів і щоденних нотаток. <code>Alt+Shift+H</code> створює нотатку у вибраній групі; <code>Alt+Shift+Z</code> згортає всі групи. <code>Ctrl+Z</code> скасовує зміни впорядкування.",
    "Стрілка біля Нової нотатки відкриває варіанти нотаток Markdown, шаблонів, сьогоднішньої нотатки, нотатки у вибраній групі та імпорту папки (<code>Alt+Shift+I</code>). Експорт усього записника в Markdown розміщено на кнопці бібліотеки бічної панелі.",
    "Позначте групу як групу шаблонів у її картці, відкритій правою кнопкою. Її нотатки з'являться в меню Нова нотатка, у пункті Нова нотатка із шаблону; можна також натиснути <code>Alt+T</code>. Переміщення нотатки до цієї групи або з неї визначає, чи є вона шаблоном.",
    "Виберіть Нова нотатка Markdown у меню Нова нотатка (<code>Alt+N</code>). Перемикач Форматований текст / Markdown у картці нотатки, відкритій правою кнопкою, перетворює по одній нотатці (<code>Alt+F</code>), попереджаючи перед перетворенням із втратами.",
    "Скористайтеся Експортувати всі нотатки як Markdown на кнопці бібліотеки бічної панелі, щоб записати весь записник у файли <code>.md</code>. Групи стануть підпапками; початкові метадані містять заголовки, мітки та дати."
  ],
  "kk": [
    "Жазбаның картасын ашу үшін оны тінтуірдің оң жақ түймесімен басыңыз: бекіту, бөлісу, экспорт, тарих, түс, тегтер және топ. Көшірмесін жасау (<code>Alt+Shift+U</code>) көшірме жасайды; Үлгі ретінде сақтау (<code>Alt+Shift+T</code>) оны үлгілер тобына көшіреді. Бекітуін, түсін, тегтерін немесе тобын бірге өзгерту үшін бірнеше жазбаны таңдаңыз.",
    "Бүйірлік жолақтағы Реттеу (<code>Alt+Shift+O</code>) іздеуі бар топтар ағашын және сұрыпталатын жазбалар тізімін ашады. Жазбаларды топтарға, топтарды ата-аналық топтарға сүйреңіз; оң жақ түйме бүйірлік жолақтағы карталарды ашады. Топ картасында атау, түс, ата-аналық топ, үлгі және күнделікті жазба рөлдері бар. <code>Alt+Shift+H</code> таңдалған топта жазба жасайды; <code>Alt+Shift+Z</code> барлық топтарды жияды. <code>Ctrl+Z</code> реттеу өзгерістерін болдырмайды.",
    "Жаңа жазба жанындағы көрсеткі Markdown жазбаларын, үлгілерді, бүгінгі жазбаны, таңдалған топтағы жазбаны және қалтаны импорттауды (<code>Alt+Shift+I</code>) ашады. Бүкіл жазба кітапшасын Markdown пішімінде экспорттау бүйірлік жолақтағы кітапхана түймесінде орналасқан.",
    "Оң жақ түймемен ашылған топ картасында оны үлгілер тобы ретінде белгілеңіз. Оның жазбалары Жаңа жазба мәзіріндегі Үлгіден жаңа жазба тармағында көрінеді; <code>Alt+T</code> де осы тізімді ашады. Жазбаны бұл топқа кіргізу немесе одан шығару оның үлгі болуын анықтайды.",
    "Жаңа жазба мәзірінен Жаңа Markdown жазбасы тармағын таңдаңыз (<code>Alt+N</code>). Жазбаның оң жақ түймемен ашылған картасындағы Пішімделген мәтін / Markdown ауыстырғышы бір жазбаны түрлендіреді (<code>Alt+F</code>), жоғалтумен түрлендіру алдында ескертеді.",
    "Бүйірлік жолақтағы кітапхана түймесінде Барлық жазбаларды Markdown ретінде экспорттау пәрменімен бүкіл кітапшаны <code>.md</code> файлдарына жазыңыз. Топтар ішкі қалталарға айналады; бастапқы метадеректер тақырыптарды, тегтерді және күндерді қамтиды."
  ],
  "vi": [
    "Nhấp chuột phải vào ghi chú để mở thẻ: ghim, chia sẻ, xuất, lịch sử, màu, thẻ gắn và nhóm. Nhân bản (<code>Alt+Shift+U</code>) tạo bản sao; Lưu làm mẫu (<code>Alt+Shift+T</code>) sao chép ghi chú vào nhóm mẫu. Chọn nhiều ghi chú để đổi trạng thái ghim, màu, thẻ gắn hoặc nhóm cùng lúc.",
    "Sắp xếp trên thanh bên (<code>Alt+Shift+O</code>) mở cây nhóm có tìm kiếm và danh sách ghi chú có thể sắp thứ tự. Kéo ghi chú vào nhóm hoặc nhóm vào nhóm cha; nhấp chuột phải mở cùng các thẻ như thanh bên. Thẻ nhóm cho phép đổi tên, màu, nhóm cha và vai trò mẫu hoặc ghi chú hằng ngày. <code>Alt+Shift+H</code> tạo ghi chú trong nhóm đã chọn; <code>Alt+Shift+Z</code> thu gọn mọi nhóm. <code>Ctrl+Z</code> hoàn tác thay đổi cách sắp xếp.",
    "Mũi tên cạnh Ghi chú mới mở các lựa chọn ghi chú Markdown, mẫu, ghi chú hôm nay, ghi chú trong nhóm đã chọn và nhập thư mục (<code>Alt+Shift+I</code>). Xuất cả sổ ghi chú sang Markdown nằm ở nút thư viện trên thanh bên.",
    "Đánh dấu một nhóm là nhóm mẫu trong thẻ mở bằng chuột phải. Các ghi chú của nhóm xuất hiện trong menu Ghi chú mới, dưới Ghi chú mới từ mẫu; bạn cũng có thể nhấn <code>Alt+T</code>. Chuyển ghi chú vào hoặc ra khỏi nhóm này quyết định nó có phải là mẫu hay không.",
    "Chọn Ghi chú Markdown mới trong menu Ghi chú mới (<code>Alt+N</code>). Điều khiển Văn bản có định dạng / Markdown trên thẻ chuột phải của ghi chú chuyển đổi từng ghi chú (<code>Alt+F</code>), có cảnh báo trước khi chuyển đổi gây mất thông tin.",
    "Dùng Xuất tất cả ghi chú sang Markdown ở nút thư viện trên thanh bên để ghi cả sổ thành các tệp <code>.md</code>. Nhóm trở thành thư mục con; siêu dữ liệu đầu tệp chứa tiêu đề, thẻ gắn và ngày."
  ],
  "ja": [
    "ノートを右クリックすると、ピン留め、共有、エクスポート、履歴、色、タグ、グループのカードが開きます。複製（<code>Alt+Shift+U</code>）でコピーを作成し、テンプレートとして保存（<code>Alt+Shift+T</code>）でテンプレートグループにコピーします。複数のノートを選択すると、ピン留め、色、タグ、グループをまとめて変更できます。",
    "サイドバーの整理（<code>Alt+Shift+O</code>）で、検索可能なグループツリーと並べ替え可能なノート一覧が開きます。ノートをグループへ、グループを親グループへドラッグできます。右クリックではサイドバーと同じカードが開きます。グループカードでは名前、色、親、テンプレート用と日次ノート用の役割を設定できます。<code>Alt+Shift+H</code>で選択したグループにノートを作成し、<code>Alt+Shift+Z</code>ですべてのグループを折りたたみます。<code>Ctrl+Z</code>で整理操作を元に戻せます。",
    "新規ノートの横の矢印から、Markdownノート、テンプレート、今日のノート、指定したグループ内のノート、フォルダーのインポート（<code>Alt+Shift+I</code>）を選べます。ノートブック全体のMarkdownエクスポートは、サイドバーのライブラリボタンにあります。",
    "グループを右クリックしてカードを開き、テンプレートグループに指定します。そのグループのノートは、新規ノートメニューのテンプレートから新規作成に表示されます。<code>Alt+T</code>でも開けます。ノートをそのグループに入れるか外に移すかで、テンプレートとして扱われるかが変わります。",
    "新規ノートメニューで新規Markdownノートを選びます（<code>Alt+N</code>）。ノートの右クリックカードにあるリッチテキスト / Markdown切り替えで、1つずつ変換できます（<code>Alt+F</code>）。情報が失われる変換の前には警告が表示されます。",
    "サイドバーのライブラリボタンから、すべてのノートをMarkdownとしてエクスポートを選ぶと、ノートブック全体を<code>.md</code>ファイルに書き出せます。グループはサブフォルダーになり、先頭のメタデータにタイトル、タグ、日付が含まれます。"
  ],
  "zh": [
    "在筆記上按滑鼠右鍵可開啟卡片：釘選、分享、匯出、歷史紀錄、色彩、標籤和群組。複製筆記（<code>Alt+Shift+U</code>）會建立副本；另存為範本（<code>Alt+Shift+T</code>）會將副本放入範本群組。選取多篇筆記即可一起變更釘選狀態、色彩、標籤或群組。",
    "側邊欄的整理（<code>Alt+Shift+O</code>）會開啟可搜尋的群組樹和可排序的筆記清單。將筆記拖入群組，或將群組拖入父群組；按滑鼠右鍵會開啟與側邊欄相同的卡片。群組卡片可設定名稱、色彩、父群組，以及範本和每日筆記用途。<code>Alt+Shift+H</code>在選取的群組內建立筆記；<code>Alt+Shift+Z</code>摺疊所有群組。<code>Ctrl+Z</code>可復原整理變更。",
    "新增筆記旁的箭頭可開啟 Markdown 筆記、範本、今日筆記、在指定群組中新增筆記，以及匯入資料夾（<code>Alt+Shift+I</code>）。整本筆記本的 Markdown 匯出位於側邊欄的資料庫按鈕中。",
    "在群組的右鍵卡片中將它標記為範本群組。其中的筆記會出現在新增筆記選單的從範本新增筆記下，也可按 <code>Alt+T</code> 開啟。將筆記移入或移出該群組，即可決定它是否作為範本。",
    "在新增筆記選單中選擇新增 Markdown 筆記（<code>Alt+N</code>）。筆記右鍵卡片的豐富文字 / Markdown 控制項一次轉換一篇筆記（<code>Alt+F</code>），並在有損轉換前顯示警告。",
    "使用側邊欄資料庫按鈕中的將所有筆記匯出為 Markdown，即可將整本筆記本寫成 <code>.md</code> 檔案。群組會成為子資料夾；檔案開頭的中繼資料包含標題、標籤和日期。"
  ],
  "zh-cn": [
    "右键单击笔记可打开卡片：置顶、分享、导出、历史记录、颜色、标签和分组。复制笔记（<code>Alt+Shift+U</code>）会创建副本；另存为模板（<code>Alt+Shift+T</code>）会将副本放入模板分组。选择多篇笔记即可一起更改置顶状态、颜色、标签或分组。",
    "侧边栏的整理（<code>Alt+Shift+O</code>）会打开可搜索的分组树和可排序的笔记列表。将笔记拖入分组，或将分组拖入父分组；右键单击会打开与侧边栏相同的卡片。分组卡片可设置名称、颜色、父分组，以及模板和每日笔记用途。<code>Alt+Shift+H</code>在所选分组内创建笔记；<code>Alt+Shift+Z</code>折叠所有分组。<code>Ctrl+Z</code>可撤销整理更改。",
    "新建笔记旁的箭头可打开 Markdown 笔记、模板、今日笔记、在指定分组中新建笔记，以及导入文件夹（<code>Alt+Shift+I</code>）。整个笔记本的 Markdown 导出位于侧边栏的资料库按钮中。",
    "在分组的右键卡片中将其标记为模板分组。其中的笔记会出现在新建笔记菜单的从模板新建笔记下，也可按 <code>Alt+T</code> 打开。将笔记移入或移出该分组，即可决定它是否作为模板。",
    "在新建笔记菜单中选择新建 Markdown 笔记（<code>Alt+N</code>）。笔记右键卡片的富文本 / Markdown 控件一次转换一篇笔记（<code>Alt+F</code>），并在有损转换前显示警告。",
    "使用侧边栏资料库按钮中的将所有笔记导出为 Markdown，即可将整个笔记本写成 <code>.md</code> 文件。分组会成为子文件夹；文件开头的元数据包含标题、标签和日期。"
  ],
  "bn": [
    "নোটে ডান ক্লিক করলে তার কার্ড খুলবে: পিন, শেয়ার, এক্সপোর্ট, ইতিহাস, রং, ট্যাগ ও গ্রুপ। প্রতিলিপি করুন (<code>Alt+Shift+U</code>) একটি কপি তৈরি করে; টেমপ্লেট হিসেবে সংরক্ষণ (<code>Alt+Shift+T</code>) সেটি টেমপ্লেট গ্রুপে কপি করে। পিন, রং, ট্যাগ বা গ্রুপ একসঙ্গে বদলাতে একাধিক নোট বাছুন।",
    "সাইডবারের সাজান (<code>Alt+Shift+O</code>) অনুসন্ধানযোগ্য গ্রুপ ট্রি ও সাজানো যায় এমন নোট তালিকা খোলে। নোট গ্রুপে বা গ্রুপ মূল গ্রুপে টেনে আনুন; ডান ক্লিকে সাইডবারের একই কার্ড খুলবে। গ্রুপ কার্ডে নাম, রং, মূল গ্রুপ এবং টেমপ্লেট ও দৈনিক নোটের ভূমিকা নির্ধারণ করা যায়। <code>Alt+Shift+H</code> নির্বাচিত গ্রুপে নোট তৈরি করে; <code>Alt+Shift+Z</code> সব গ্রুপ গুটিয়ে দেয়। <code>Ctrl+Z</code> বিন্যাসের পরিবর্তন পূর্বাবস্থায় ফেরায়।",
    "নতুন নোটের পাশের তীর থেকে Markdown নোট, টেমপ্লেট, আজকের নোট, পছন্দের গ্রুপে নোট এবং ফোল্ডার ইম্পোর্ট (<code>Alt+Shift+I</code>) খোলা যায়। পুরো নোটবুকের Markdown এক্সপোর্ট সাইডবারের লাইব্রেরি বোতামে আছে।",
    "ডান ক্লিকে খোলা গ্রুপ কার্ডে সেটিকে টেমপ্লেট গ্রুপ হিসেবে চিহ্নিত করুন। তার নোটগুলো নতুন নোট মেনুতে টেমপ্লেট থেকে নতুন নোটের অধীনে দেখা যাবে; <code>Alt+T</code> চাপলেও খোলে। নোট এই গ্রুপে আনা বা বের করা নির্ধারণ করে সেটি টেমপ্লেট কি না।",
    "নতুন নোট মেনুতে নতুন Markdown নোট বাছুন (<code>Alt+N</code>)। নোটের ডান-ক্লিক কার্ডের সমৃদ্ধ লেখা / Markdown নিয়ন্ত্রণ একবারে একটি নোট রূপান্তর করে (<code>Alt+F</code>), তথ্য হারানোর রূপান্তরের আগে সতর্ক করে।",
    "সাইডবারের লাইব্রেরি বোতামে সব নোট Markdown হিসেবে এক্সপোর্ট ব্যবহার করে পুরো নোটবুক <code>.md</code> ফাইলে লিখুন। গ্রুপগুলো সাবফোল্ডার হয়; শুরুর মেটাডেটায় শিরোনাম, ট্যাগ ও তারিখ থাকে।"
  ]
};
  var keys = ["s170_cards","s170_groups","s170_new","hp_tpl_1","hp_md_1","hp_vault_1"];
  Object.keys(updates).forEach(function (locale) {
    if (!I18N[locale]) throw new Error('Missing website locale: ' + locale);
    updates[locale].forEach(function (text, index) { I18N[locale][keys[index]] = text; });
  });

  var extra = {
  "it": [
    "Le note si ordinano per data di creazione, titolo o ordine personalizzato. <code>Ctrl+F10</code> cambia modalità; il pulsante di ordinamento apre il menu e mostra modalità e direzione correnti.",
    "<code>F4</code> alterna Sorgente, Renderizzato e Diviso per Markdown e HTML. Il pulsante con l'occhio elenca le stesse viste. La scheda Informazioni controlla il rilevamento automatico. L'anteprima WPF nativa segue tema e grana; gli script non vengono eseguiti.",
    "Usa <code>Ctrl+Shift+E</code> o l'interruttore Sintassi nella scheda aperta con il clic destro nell'editor. Il linguaggio viene rilevato per paragrafo: testo e frammenti di codice possono convivere senza scegliere un linguaggio."
  ],
  "es": [
    "Las notas se ordenan por fecha de creación, título u orden personalizado. <code>Ctrl+F10</code> cambia de modo; el botón de ordenación abre el menú y muestra el modo y la dirección actuales.",
    "<code>F4</code> alterna Fuente, Renderizado y Dividido para Markdown y HTML. El botón del ojo ofrece las mismas vistas. La tarjeta Acerca de controla la detección automática. La vista previa nativa de WPF sigue el tema y el grano; los scripts no se ejecutan.",
    "Usa <code>Ctrl+Shift+E</code> o el interruptor Sintaxis en la tarjeta de clic derecho del editor. El lenguaje se detecta por párrafo: puedes mezclar texto y fragmentos de código sin elegir un lenguaje."
  ],
  "de": [
    "Notizen lassen sich nach Erstellungsdatum, Titel oder eigener Reihenfolge sortieren. <code>Ctrl+F10</code> wechselt den Modus; die Sortierschaltfläche öffnet das Menü und zeigt den aktuellen Modus samt Richtung.",
    "<code>F4</code> wechselt für Markdown und HTML zwischen Quelle, Gerendert und Geteilt. Die Augenschaltfläche bietet dieselben Ansichten. Die automatische Erkennung wird auf der Info-Karte eingestellt. Die native WPF-Vorschau folgt Design und Körnung; Skripte werden nicht ausgeführt.",
    "Verwende <code>Ctrl+Shift+E</code> oder den Syntax-Schalter auf der Rechtsklickkarte des Editors. Die Sprache wird pro Absatz erkannt, sodass Text und Codeausschnitte ohne Sprachauswahl gemischt werden können."
  ],
  "fr": [
    "Les notes se trient par date de création, titre ou ordre personnalisé. <code>Ctrl+F10</code> change de mode ; le bouton de tri ouvre le menu et indique le mode et le sens actuels.",
    "<code>F4</code> alterne Source, Rendu et Partagé pour Markdown et HTML. Le bouton en forme d'œil propose les mêmes vues. La fiche À propos contrôle la détection automatique. L'aperçu WPF natif suit le thème et le grain ; les scripts ne s'exécutent jamais.",
    "Utilisez <code>Ctrl+Shift+E</code> ou le commutateur Syntaxe de la fiche accessible par clic droit dans l'éditeur. Le langage est détecté par paragraphe : texte et extraits de code peuvent se mélanger sans choisir de langage."
  ],
  "pt": [
    "As notas são ordenadas por data de criação, título ou ordem personalizada. <code>Ctrl+F10</code> alterna os modos; o botão de ordenação abre o menu e mostra o modo e a direção atuais.",
    "<code>F4</code> alterna Fonte, Renderizado e Dividido para Markdown e HTML. O botão do olho oferece as mesmas visualizações. O cartão Sobre controla a detecção automática. A prévia nativa do WPF segue o tema e a granulação; scripts nunca são executados.",
    "Use <code>Ctrl+Shift+E</code> ou a opção Sintaxe no cartão aberto com o botão direito no editor. O idioma é detectado por parágrafo: texto e trechos de código podem ser misturados sem escolher uma linguagem."
  ],
  "nb": [
    "Noter sorteres etter opprettelsesdato, tittel eller egen rekkefølge. <code>Ctrl+F10</code> bytter modus; sorteringsknappen åpner menyen og viser gjeldende modus og retning.",
    "<code>F4</code> veksler mellom Kilde, Gjengitt og Delt for Markdown og HTML. Øyeknappen viser de samme visningene. Om-kortet styrer automatisk gjenkjenning. Den innebygde WPF-forhåndsvisningen følger tema og kornstruktur; skript kjøres aldri.",
    "Bruk <code>Ctrl+Shift+E</code> eller Syntaks-bryteren på redigeringsfeltets høyreklikkort. Språket gjenkjennes per avsnitt, så tekst og kodeutdrag kan blandes uten å velge språk."
  ],
  "sl": [
    "Zapiski se razvrščajo po datumu nastanka, naslovu ali vrstnem redu po meri. <code>Ctrl+F10</code> menja načine; gumb za razvrščanje odpre meni ter pokaže trenutni način in smer.",
    "<code>F4</code> preklaplja med Izvorno, Izrisano in Razdeljeno za Markdown in HTML. Gumb z očesom ponuja iste poglede. Kartica O programu nadzoruje samodejno zaznavanje. Izvorni predogled WPF sledi temi in zrnatosti; skripti se nikoli ne izvajajo.",
    "Uporabite <code>Ctrl+Shift+E</code> ali stikalo Sintaksa na kartici urejevalnika, odprti z desnim klikom. Jezik se zaznava za vsak odstavek, zato lahko mešate besedilo in odseke kode brez izbire jezika."
  ],
  "pl": [
    "Notatki są sortowane według daty utworzenia, tytułu lub własnej kolejności. <code>Ctrl+F10</code> przełącza tryby; przycisk sortowania otwiera menu i pokazuje bieżący tryb oraz kierunek.",
    "<code>F4</code> przełącza Źródło, Widok wyrenderowany i Widok dzielony dla Markdown i HTML. Przycisk oka oferuje te same widoki. Karta O programie steruje automatycznym wykrywaniem. Natywny podgląd WPF uwzględnia motyw i ziarno; skrypty nigdy się nie wykonują.",
    "Użyj <code>Ctrl+Shift+E</code> lub przełącznika Składnia na karcie edytora otwieranej prawym przyciskiem. Język jest wykrywany dla każdego akapitu, więc można łączyć tekst z fragmentami kodu bez wybierania języka."
  ],
  "cs": [
    "Poznámky se řadí podle data vytvoření, názvu nebo vlastního pořadí. <code>Ctrl+F10</code> přepíná režimy; tlačítko řazení otevře nabídku a ukazuje aktuální režim i směr.",
    "<code>F4</code> přepíná Zdroj, Vykreslené a Rozdělené pro Markdown a HTML. Tlačítko oka nabízí stejné pohledy. Karta O aplikaci ovládá automatické rozpoznávání. Nativní náhled WPF se řídí motivem a zrnitostí; skripty se nikdy nespouštějí.",
    "Použijte <code>Ctrl+Shift+E</code> nebo přepínač Syntaxe na kartě editoru otevřené pravým tlačítkem. Jazyk se rozpoznává po odstavcích, takže lze míchat text s úryvky kódu bez výběru jazyka."
  ],
  "hu": [
    "A jegyzetek létrehozási dátum, cím vagy egyéni sorrend szerint rendezhetők. A <code>Ctrl+F10</code> vált a módok között; a rendezés gomb megnyitja a menüt, és mutatja az aktuális módot és irányt.",
    "Az <code>F4</code> a Forrás, Megjelenített és Osztott nézetet váltja Markdown és HTML esetén. A szem gomb ugyanezeket a nézeteket kínálja. Az automatikus felismerés a Névjegy kártyán állítható. A natív WPF-előnézet követi a témát és a szemcsézettséget; parancsfájlok sosem futnak.",
    "Használd a <code>Ctrl+Shift+E</code> billentyűt vagy a szerkesztő jobb kattintásos kártyájának Szintaxis kapcsolóját. A nyelv felismerése bekezdésenként történik, így szöveg és kódrészletek nyelvválasztás nélkül keverhetők."
  ],
  "tr": [
    "Notlar oluşturulma tarihine, başlığa veya özel sıraya göre sıralanır. <code>Ctrl+F10</code> modlar arasında geçiş yapar; sıralama düğmesi menüyü açar ve geçerli modu ve yönü gösterir.",
    "<code>F4</code>, Markdown ve HTML için Kaynak, İşlenmiş ve Bölünmüş görünümleri arasında geçiş yapar. Göz düğmesi aynı görünümleri listeler. Hakkında kartı otomatik algılamayı denetler. Yerel WPF önizlemesi temayı ve gren dokusunu izler; betikler hiçbir zaman çalıştırılmaz.",
    "<code>Ctrl+Shift+E</code> veya düzenleyicinin sağ tıklama kartındaki Sözdizimi anahtarını kullanın. Dil paragraf başına algılanır; dil seçmeden metin ve kod parçalarını karıştırabilirsiniz."
  ],
  "ru": [
    "Заметки сортируются по дате создания, заголовку или собственному порядку. <code>Ctrl+F10</code> переключает режимы; кнопка сортировки открывает меню и показывает текущий режим и направление.",
    "<code>F4</code> переключает Исходник, Отображение и Разделённый вид для Markdown и HTML. Кнопка с глазом предлагает те же виды. Карточка О программе управляет автоматическим распознаванием. Встроенный предпросмотр WPF следует теме и зернистости; скрипты никогда не запускаются.",
    "Используйте <code>Ctrl+Shift+E</code> или переключатель Синтаксис в карточке редактора, открытой правой кнопкой. Язык определяется для каждого абзаца, поэтому текст и фрагменты кода можно смешивать без выбора языка."
  ],
  "uk": [
    "Нотатки сортуються за датою створення, заголовком або власним порядком. <code>Ctrl+F10</code> перемикає режими; кнопка сортування відкриває меню й показує поточний режим і напрямок.",
    "<code>F4</code> перемикає Джерело, Відображення та Розділений вигляд для Markdown і HTML. Кнопка з оком пропонує ті самі вигляди. Картка Про програму керує автоматичним розпізнаванням. Вбудований попередній перегляд WPF дотримується теми й зернистості; сценарії ніколи не виконуються.",
    "Скористайтеся <code>Ctrl+Shift+E</code> або перемикачем Синтаксис у картці редактора, відкритій правою кнопкою. Мова визначається для кожного абзацу, тому текст і фрагменти коду можна поєднувати без вибору мови."
  ],
  "kk": [
    "Жазбалар жасалған күні, тақырыбы немесе өз ретіңіз бойынша сұрыпталады. <code>Ctrl+F10</code> режимдерді ауыстырады; сұрыптау түймесі мәзірді ашып, ағымдағы режим мен бағытты көрсетеді.",
    "<code>F4</code> Markdown және HTML үшін Бастапқы мәтін, Көрсетілген және Бөлінген көріністерін ауыстырады. Көз түймесі сол көріністерді ұсынады. Бағдарлама туралы картасы автоматты анықтауды басқарады. WPF алдын ала көрінісі тақырып пен түйіршікті құрылымды сақтайды; сценарийлер ешқашан орындалмайды.",
    "<code>Ctrl+Shift+E</code> немесе редактордың оң жақ түймемен ашылған картасындағы Синтаксис ауыстырғышын қолданыңыз. Тіл әр абзац үшін анықталады, сондықтан тіл таңдамай мәтін мен код үзінділерін араластыруға болады."
  ],
  "vi": [
    "Ghi chú được sắp theo ngày tạo, tiêu đề hoặc thứ tự tùy chọn. <code>Ctrl+F10</code> chuyển chế độ; nút sắp xếp mở menu và hiển thị chế độ cùng chiều sắp xếp hiện tại.",
    "<code>F4</code> chuyển giữa Nguồn, Kết xuất và Chia đôi cho Markdown và HTML. Nút hình mắt liệt kê cùng các chế độ xem. Thẻ Giới thiệu điều khiển tính năng tự nhận diện. Bản xem trước WPF gốc theo chủ đề và hiệu ứng hạt; tập lệnh không bao giờ chạy.",
    "Dùng <code>Ctrl+Shift+E</code> hoặc công tắc Cú pháp trên thẻ chuột phải của trình soạn thảo. Ngôn ngữ được nhận diện theo từng đoạn, nên có thể trộn văn bản và mã mà không cần chọn ngôn ngữ."
  ],
  "ja": [
    "ノートは作成日、タイトル、独自の順序で並べ替えられます。<code>Ctrl+F10</code>でモードを切り替えます。並べ替えボタンはメニューを開き、現在のモードと方向を表示します。",
    "<code>F4</code>でMarkdownとHTMLのソース、表示、分割を切り替えます。目のボタンからも同じ表示を選べます。自動検出はバージョン情報カードで設定します。ネイティブWPFプレビューはテーマと粒子効果に従い、スクリプトは実行しません。",
    "<code>Ctrl+Shift+E</code>、またはエディターの右クリックカードの構文スイッチを使います。言語は段落ごとに検出されるため、言語を選ばずに文章とコードを混在させられます。"
  ],
  "zh": [
    "筆記可依建立日期、標題或自訂順序排序。<code>Ctrl+F10</code>可切換模式；排序按鈕會開啟選單，並顯示目前的模式和方向。",
    "<code>F4</code>可切換 Markdown 和 HTML 的原始碼、呈現和分割檢視。眼睛按鈕也提供相同的檢視。自動偵測可在關於卡片中設定。原生 WPF 預覽會配合佈景主題和顆粒效果；絕不執行指令碼。",
    "使用 <code>Ctrl+Shift+E</code> 或編輯器右鍵卡片中的語法開關。程式語言會逐段偵測，因此文字和程式碼片段可以混用，無須選擇語言。"
  ],
  "zh-cn": [
    "笔记可按创建日期、标题或自定义顺序排序。<code>Ctrl+F10</code>可切换模式；排序按钮会打开菜单，并显示当前模式和方向。",
    "<code>F4</code>可切换 Markdown 和 HTML 的源代码、渲染和分栏视图。眼睛按钮也提供相同的视图。自动检测可在关于卡片中设置。原生 WPF 预览会配合主题和颗粒效果；绝不运行脚本。",
    "使用 <code>Ctrl+Shift+E</code> 或编辑器右键卡片中的语法开关。编程语言会逐段检测，因此文字和代码片段可以混用，无须选择语言。"
  ],
  "bn": [
    "নোট তৈরির তারিখ, শিরোনাম বা নিজের নির্ধারিত ক্রমে সাজানো যায়। <code>Ctrl+F10</code> মোড বদলায়; সাজানোর বোতাম মেনু খোলে এবং বর্তমান মোড ও দিক দেখায়।",
    "<code>F4</code> Markdown ও HTML-এর উৎস, রেন্ডার করা এবং বিভক্ত দৃশ্য বদলায়। চোখের বোতামেও একই দৃশ্যের তালিকা আছে। পরিচিতি কার্ডে স্বয়ংক্রিয় শনাক্তকরণ নিয়ন্ত্রণ করা যায়। নেটিভ WPF পূর্বরূপ থিম ও গ্রেইন অনুসরণ করে; স্ক্রিপ্ট কখনো চালানো হয় না।",
    "<code>Ctrl+Shift+E</code> বা এডিটরের ডান-ক্লিক কার্ডের সিনট্যাক্স সুইচ ব্যবহার করুন। প্রতি অনুচ্ছেদে ভাষা শনাক্ত হয়, তাই ভাষা না বেছেই সাধারণ লেখা ও কোড একসঙ্গে রাখা যায়।"
  ]
};
  var extraKeys = ["hp_13","site_preview_current","hp_111"];
  Object.keys(extra).forEach(function (locale) {
    extra[locale].forEach(function (text, index) { I18N[locale][extraKeys[index]] = text; });
  });
}());
