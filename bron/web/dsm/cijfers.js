window.DSMDATA = window.DSMDATA || {};
// Bron: ten Have e.a. (2023), World Psychiatry 22:275–285, NEMESIS-3, Trimbos-instituut.
DSMDATA.prevalentie = [
{"stoornis":"Enige psychische aandoening","groep":"Totaal","ooit":48.4,"m12":25.9,"ooit_man":44.9,"ooit_vrouw":51.8,"m12_man":24.0,"m12_vrouw":27.8},
{"stoornis":"Enige stemmingsstoornis","groep":"Stemming","ooit":27.6,"m12":9.8,"ooit_man":22.1,"ooit_vrouw":33.0,"m12_man":8.1,"m12_vrouw":11.5},
{"stoornis":"Depressieve stoornis","groep":"Stemming","ooit":24.9,"m12":8.5,"ooit_man":19.2,"ooit_vrouw":30.5,"m12_man":6.7,"m12_vrouw":10.4},
{"stoornis":"Persisterende depressieve stoornis","groep":"Stemming","ooit":9.1,"m12":3.5,"ooit_man":6.7,"ooit_vrouw":11.4,"m12_man":2.6,"m12_vrouw":4.4},
{"stoornis":"Bipolaire stoornis","groep":"Stemming","ooit":2.1,"m12":1.2,"ooit_man":2.5,"ooit_vrouw":1.8,"m12_man":1.3,"m12_vrouw":1.1},
{"stoornis":"Enige angststoornis","groep":"Angst","ooit":28.6,"m12":15.2,"ooit_man":21.8,"ooit_vrouw":35.5,"m12_man":11.0,"m12_vrouw":19.4},
{"stoornis":"Paniekstoornis","groep":"Angst","ooit":5.6,"m12":2.2,"ooit_man":3.7,"ooit_vrouw":7.5,"m12_man":1.5,"m12_vrouw":2.9},
{"stoornis":"Agorafobie","groep":"Angst","ooit":4.0,"m12":1.9,"ooit_man":2.4,"ooit_vrouw":5.5,"m12_man":1.1,"m12_vrouw":2.7},
{"stoornis":"Sociale angststoornis","groep":"Angst","ooit":13.1,"m12":5.6,"ooit_man":10.8,"ooit_vrouw":15.3,"m12_man":4.6,"m12_vrouw":6.7},
{"stoornis":"Specifieke fobie","groep":"Angst","ooit":11.8,"m12":7.8,"ooit_man":7.5,"ooit_vrouw":16.1,"m12_man":4.6,"m12_vrouw":11.0},
{"stoornis":"Gegeneraliseerde angststoornis","groep":"Angst","ooit":9.5,"m12":3.8,"ooit_man":6.5,"ooit_vrouw":12.6,"m12_man":2.8,"m12_vrouw":4.8},
{"stoornis":"Enige middelenstoornis","groep":"Middelen","ooit":16.7,"m12":7.1,"ooit_man":22.5,"ooit_vrouw":11.0,"m12_man":9.5,"m12_vrouw":4.8},
{"stoornis":"Alcoholgebruiksstoornis","groep":"Middelen","ooit":12.8,"m12":5.4,"ooit_man":17.8,"ooit_vrouw":7.9,"m12_man":7.5,"m12_vrouw":3.3},
{"stoornis":"Drugsgebruiksstoornis","groep":"Middelen","ooit":6.6,"m12":2.3,"ooit_man":8.5,"ooit_vrouw":4.7,"m12_man":2.8,"m12_vrouw":1.7},
{"stoornis":"Cannabisgebruiksstoornis","groep":"Middelen","ooit":3.9,"m12":1.3,"ooit_man":5.9,"ooit_vrouw":1.9,"m12_man":1.9,"m12_vrouw":0.6},
{"stoornis":"ADHD","groep":"ADHD","ooit":3.6,"m12":3.2,"ooit_man":4.3,"ooit_vrouw":3.0,"m12_man":3.7,"m12_vrouw":2.7}
];
DSMDATA.leeftijd = [
{"leeftijd":"18–24","m12":39.6},{"leeftijd":"25–34","m12":35.2},{"leeftijd":"35–44","m12":27.7},
{"leeftijd":"45–54","m12":21.8},{"leeftijd":"55–64","m12":21.4},{"leeftijd":"65–75","m12":13.9}
];
DSMDATA.trend = [
{"groep":"Stemmingsstoornis","n2":6.0,"n3":10.8},{"groep":"Angststoornis","n2":10.1,"n3":15.6},
{"groep":"Middelenstoornis","n2":5.5,"n3":7.1},{"groep":"Enige aandoening","n2":17.4,"n3":26.1}
];
DSMDATA.hoofdstukken = [
{"nr":1,"hoofdstuk":"Neurobiologische ontwikkelingsstoornissen","voorbeelden":"ADHD, autismespectrumstoornis, verstandelijke beperking, specifieke leerstoornis, ticstoornissen","relevantie":"Soms basis-GGZ","toelichting":"Diagnostiek ADHD/ASS bij volwassenen vaak specialistisch; bij bekende diagnose kan comorbide klachten in BGGZ."},
{"nr":2,"hoofdstuk":"Schizofreniespectrum- en andere psychotische stoornissen","voorbeelden":"Schizofrenie, schizoaffectieve stoornis, waanstoornis, kortdurende psychotische stoornis","relevantie":"Meestal S-GGZ / verwijzen","toelichting":"Psychotische kenmerken: verwijzen naar S-GGZ/FACT."},
{"nr":3,"hoofdstuk":"Bipolaire-stemmingsstoornissen en verwante stoornissen","voorbeelden":"Bipolaire-I-stoornis, bipolaire-II-stoornis, cyclothyme stoornis","relevantie":"Meestal S-GGZ / verwijzen","toelichting":"Bij (hypo)manie in voorgeschiedenis: let op bij depressiebehandeling."},
{"nr":4,"hoofdstuk":"Depressieve-stemmingsstoornissen","voorbeelden":"Depressieve stoornis, persisterende depressieve stoornis, premenstruele stemmingsstoornis","relevantie":"Vaak basis-GGZ","toelichting":"Kernterrein basis-GGZ bij lichte tot matige ernst."},
{"nr":5,"hoofdstuk":"Angststoornissen","voorbeelden":"Paniekstoornis, agorafobie, sociale angststoornis, specifieke fobie, gegeneraliseerde angststoornis, separatieangststoornis","relevantie":"Vaak basis-GGZ","toelichting":"Meest voorkomende hoofdgroep in Nederland."},
{"nr":6,"hoofdstuk":"Obsessieve-compulsieve en verwante stoornissen","voorbeelden":"Obsessieve-compulsieve stoornis, morfodysfore stoornis, verzamelstoornis, trichotillomanie, excoriatiestoornis","relevantie":"Soms basis-GGZ","toelichting":"Lichte OCS kan in BGGZ; ernstige of chronische vormen meestal S-GGZ."},
{"nr":7,"hoofdstuk":"Trauma- en stressorgerelateerde stoornissen","voorbeelden":"PTSS, acute stressstoornis, aanpassingsstoornis, langdurige rouwstoornis (toegevoegd in DSM-5-TR)","relevantie":"Vaak basis-GGZ","toelichting":"Enkelvoudige PTSS goed te behandelen in BGGZ; complex trauma vaak S-GGZ. Aanpassingsstoornis valt doorgaans niet onder de basisverzekering."},
{"nr":8,"hoofdstuk":"Dissociatieve stoornissen","voorbeelden":"Dissociatieve identiteitsstoornis, dissociatieve amnesie, depersonalisatie-derealisatiestoornis","relevantie":"Meestal S-GGZ / verwijzen","toelichting":""},
{"nr":9,"hoofdstuk":"Somatisch-symptoomstoornis en verwante stoornissen","voorbeelden":"Somatisch-symptoomstoornis, ziekteangststoornis, functioneel-neurologische-symptoomstoornis (conversie)","relevantie":"Soms basis-GGZ","toelichting":"Vaak in samenwerking met huisarts/medisch specialist."},
{"nr":10,"hoofdstuk":"Voedings- en eetstoornissen","voorbeelden":"Anorexia nervosa, boulimia nervosa, eetbuistoornis, ARFID","relevantie":"Meestal S-GGZ / verwijzen","toelichting":"Eetstoornissen meestal naar gespecialiseerde zorg."},
{"nr":11,"hoofdstuk":"Stoornissen in de zindelijkheid","voorbeelden":"Enuresis, encopresis","relevantie":"Buiten GGZ-behandeling / niet verzekerd","toelichting":"Vooral kind/jeugd."},
{"nr":12,"hoofdstuk":"Slaap-waakstoornissen","voorbeelden":"Insomniastoornis, hypersomnolentiestoornis, narcolepsie, slaapapneu, parasomnieën","relevantie":"Soms basis-GGZ","toelichting":"Insomnie vaak als comorbide klacht; CGT-I. Medische slaapstoornissen via huisarts/slaapcentrum."},
{"nr":13,"hoofdstuk":"Seksuele disfuncties","voorbeelden":"Erectiestoornis, vrouwelijke seksuele-interesse/opwindingsstoornis, vroegtijdige ejaculatie","relevantie":"Soms basis-GGZ","toelichting":"Vaak seksuoloog."},
{"nr":14,"hoofdstuk":"Genderdysforie","voorbeelden":"Genderdysforie bij adolescenten en volwassenen","relevantie":"Meestal S-GGZ / verwijzen","toelichting":"Gespecialiseerde genderzorg."},
{"nr":15,"hoofdstuk":"Disruptieve, impulsbeheersings- en andere gedragsstoornissen","voorbeelden":"Periodiek explosieve stoornis, oppositioneel-opstandige stoornis, normoverschrijdend-gedragsstoornis","relevantie":"Meestal S-GGZ / verwijzen","toelichting":""},
{"nr":16,"hoofdstuk":"Middelgerelateerde en verslavingsstoornissen","voorbeelden":"Alcoholgebruiksstoornis, cannabisgebruiksstoornis, andere middelen, gokstoornis","relevantie":"Soms basis-GGZ","toelichting":"Lichte stoornis soms in BGGZ; matig/ernstig naar verslavingszorg."},
{"nr":17,"hoofdstuk":"Neurocognitieve stoornissen","voorbeelden":"Delirium, (lichte) neurocognitieve stoornis o.a. door Alzheimer, vasculair","relevantie":"Buiten GGZ-behandeling / niet verzekerd","toelichting":"Dementie valt niet onder GGZ-behandeling in de basisverzekering; via huisarts/geheugenpoli."},
{"nr":18,"hoofdstuk":"Persoonlijkheidsstoornissen","voorbeelden":"Borderline, vermijdende, afhankelijke, obsessieve-compulsieve, narcistische, antisociale persoonlijkheidsstoornis","relevantie":"Meestal S-GGZ / verwijzen","toelichting":"Persoonlijkheidsproblematiek kleurt behandeling; stoornis zelf meestal S-GGZ."},
{"nr":19,"hoofdstuk":"Parafiele stoornissen","voorbeelden":"Voyeurisme, exhibitionisme, pedofiele stoornis e.a.","relevantie":"Meestal S-GGZ / verwijzen","toelichting":"Gespecialiseerde (forensische) zorg."},
{"nr":20,"hoofdstuk":"Andere psychische stoornissen","voorbeelden":"Andere gespecificeerde / ongespecificeerde psychische stoornis","relevantie":"Soms basis-GGZ","toelichting":""},
{"nr":21,"hoofdstuk":"Bewegingsstoornissen door medicatie e.a.","voorbeelden":"Neuroleptica-geïnduceerd parkinsonisme, tardieve dyskinesie","relevantie":"Buiten GGZ-behandeling / niet verzekerd","toelichting":"Medisch."},
{"nr":22,"hoofdstuk":"Andere problemen die een reden voor zorg kunnen zijn","voorbeelden":"Z-codes: relatieproblemen, problemen gerelateerd aan werk, rouw, huisvesting, misbruik","relevantie":"Buiten GGZ-behandeling / niet verzekerd","toelichting":"Kunnen genoteerd worden naast een classificatie; zelf geen verzekerde zorg."}
];
