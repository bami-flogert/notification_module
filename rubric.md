# LU1 Applicatie Integratie - Beroepsproduct

## Inleiding
Via deze link lever je de uitwerking in van de groepsopdracht voor LU1 Applicatie integratie.

### Voorwaardelijk voor presentatie
De volgende onderdelen zijn vereist om deel te kunnen nemen aan de presentatie.

- Er is een code repository ingeleverd. Lever hier de code in die je downloadt vanuit je repository, zonder eventueel geïmporteerde libraries of gegenereerde temporary files.
- Code repository bevat een readme.md waarin staat beschreven hoe de oplossing kan worden gestart inclusief voorbeeld requests
- Code repository bevat een ADR directory met markdown files van ADR's. De eerste is de verantwoording voor een apart component zoals aangegeven in de workshop. Er is een ADR over de observability stack.
- Er is een realisatie transparantie logboek met daarin de gebruikte tools, en bij gebruik van AI-tooling relevante voorbeelden (prompts)
- Er zijn visualisaties van de systeem- en applicatiearchitectuur en het bedrijfsproces aangeleverd.

---

## Rubric: LU1 Applicatie-integratie Beroepsproduct v1.0

### 1. Functionele en Non-Functionele Requirements

| Niveau | Beschrijving |
|--------|-------------|
| **Onvoldoende** | De projectgroep kan onvoldoende toelichten hoe bepaalde (niet) functionele requirements terugkomen in het ontwerp. |
| **Voldoende** | De projectgroep kan de functionele en non-functionele requirements toelichten op basis van het ontwerp en toont waar nodig code-voorbeelden ter illustratie. |
| **Goed** | Bevat alle onderdelen van "Voldoende" en de projectgroep licht overwogen alternatieven toe en beschrijft op basis van welke criteria deze zijn afgevallen. |

### 2. Schaalbaarheid en Robuustheid

| Niveau | Beschrijving |
|--------|-------------|
| **Onvoldoende** | De schaalbaarheid en robuustheid van de oplossing kan onvoldoende worden toegelicht of bewezen. |
| **Voldoende** | De schaalbaarheid en robuustheid kan worden toegelicht op basis van een failure-mode effect analysis die overeenkomt met de opgeleverde code en architectuur en kan daarnaast worden bewezen met behulp van een performancerapportage en realtime monitoring van de huidige staat. |
| **Goed** | Bevat alle onderdelen van "Voldoende" en de projectgroep toont aan welke test- en verbeterstappen hebben plaatsgevonden om de performance en robuustheid te verbeteren. |

### 3. Persistentiemechanismen en Toekomstbestendigheid

| Niveau | Beschrijving |
|--------|-------------|
| **Onvoldoende** | De persistentiemechanismen zijn niet conform best practices t.a.v. toekomstbestendigheid geïntegreerd. De implementatie biedt geen ruimte voor multi-tenancy of voor andere bedrijfsprocessen om aan te sluiten op het gerealiseerde proces. |
| **Voldoende** | De oplossing is bestand tegen wijzigingen doordat versiebeheer is toegepast en/of er rekening is gehouden met schemawijzigingen. |
| **Goed** | De implementatie volgt ontwerpprincipes zodat multi-tenancy en andere bedrijfsprocessen in de toekomst gefaciliteerd kunnen worden. Bevat alle onderdelen van "Voldoende" en houdt daarnaast rekening met uitzonderingsscenario's. De projectgroep onderbouwt expliciet uitbreidbaarheid aan de hand van ontwerpprincipes. |

### 4. Testing

| Niveau | Beschrijving |
|--------|-------------|
| **Onvoldoende** | Er zijn geen unit-, integratie- en/of systeemtesten aanwezig, of de kwaliteit daarvan is onvoldoende om de werking van het systeem te valideren. |
| **Voldoende** | Er zijn unit-tests aanwezig die de werking valideren. Er zijn geautomatiseerde tests aanwezig waarmee de werking en de basis betrouwbaarheid lokaal kan worden aangetoond. |
| **Goed** | Bevat alle onderdelen van "Voldoende" en daarnaast zijn er additionele testmethodieken gehanteerd die de werking van onderbouwde uitzonderingsscenario's en/of de kwaliteit op additionele kenmerken (security, architectuur) geautomatiseerd valideren. |

### 5. Tooling en Reflectie

| Niveau | Beschrijving |
|--------|-------------|
| **Onvoldoende** | Beschrijft welke tools (IDE's, Coding Agents of tools) zijn gebruikt en waarom, maar zonder diepgaande reflectie op de toegevoegde waarde of kosten. |
| **Voldoende** | Beschrijft welke tools zijn gebruikt, waarom, en reflecteert op de toegevoegde waarde (bijv. tijdwinst, kwaliteit) en kosten (bijv. iteraties, debugtijd). |
| **Goed** | Bevat alle onderdelen van "Voldoende" en biedt concrete voorbeelden en verbeterpunten die bij toekomstige soortgelijke projecten kunnen worden gehanteerd. |

---

## Scores

**Totale Score:** / 100

**Overall Score:**
- [ ] Onvoldoende
- [ ] Voldoende
- [ ] Goed

---

**Opmerking:** Dit is een evaluatierubric voor het beroepsproduct van LU1 Applicatie Integratie.