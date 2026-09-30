<!-- help-article
{
  "corpus": "help",
  "id": "timezones",
  "lookup": "timezones",
  "aliases": [
    "@tz"
  ],
  "sections": [
    {
      "id": "historical-catalog",
      "heading": "Historical IANA catalog (2011n)",
      "lookup": "timezones historical catalog"
    }
  ],
  "redirects": {
    "TIMEZONES2": "timezones historical catalog",
    "TIMEZONES3": "timezones historical catalog",
    "TIMEZONES4": "timezones historical catalog",
    "TIMEZONES5": "timezones historical catalog",
    "TIMEZONES6": "timezones historical catalog",
    "TIMEZONES7": "timezones historical catalog",
    "TIMEZONES8": "timezones historical catalog",
    "TIMEZONES9": "timezones historical catalog",
    "TIMEZONES10": "timezones historical catalog",
    "TIMEZONES11": "timezones historical catalog",
    "TIMEZONES12": "timezones historical catalog",
    "TIMEZONES13": "timezones historical catalog"
  }
}
-->
# Timezones

The time(), timefmt() and convsecs() functions have an optional time zone argument that's used for formatting the time. Without this time zone specified, the one the game's server is running under is used.

If the time zone argument is a dbref, the contents of that object's @TZ attribute is used as the zone. The attribute is not evaluated. If the object doesn't have this attribute, the server's time zone will be used.

If it's the string 'UTC', that time zone is used instead of the local one. If it's a number between -24 and +24, it adds that many hours from UTC/GMT. Fractional times are supported, e.g., -1.5 hours.

If the MUSH supports it (See @config compile), symbolic time zone names can also be used.

valid(timezone, `<tz>`) tests if `<tz>` can be used as a timezone.

The following catalog is a preserved historical snapshot, not the server's current list. Use `valid(timezone,<tz>)` to check a name against the installed timezone data. The current IANA database is available at https://www.iana.org/time-zones.

## Historical IANA catalog (2011n)

This is a list of IANA time zones names as of version 2011n of their database. See http://www.iana.org/time-zones for more information and sources.

Africa/Abidjan                     Africa/Accra<br>
Africa/Addis_Ababa                 Africa/Algiers<br>
Africa/Asmara                      Africa/Asmera<br>
Africa/Bamako                      Africa/Bangui<br>
Africa/Banjul                      Africa/Bissau<br>
Africa/Blantyre                    Africa/Brazzaville<br>
Africa/Bujumbura                   Africa/Cairo<br>
Africa/Casablanca                  Africa/Ceuta<br>
Africa/Conakry                     Africa/Dakar<br>
Africa/Dar_es_Salaam               Africa/Djibouti<br>
Africa/Douala                      Africa/El_Aaiun<br>
Africa/Freetown                    Africa/Gaborone<br>
Africa/Harare                      Africa/Johannesburg<br>
Africa/Juba                        Africa/Kampala<br>
Africa/Khartoum                    Africa/Kigali<br>
Africa/Kinshasa                    Africa/Lagos<br>
Africa/Libreville                  Africa/Lome<br>
Africa/Luanda                      Africa/Lubumbashi<br>
Africa/Lusaka                      Africa/Malabo<br>
Africa/Maputo                      Africa/Maseru<br>
Africa/Mbabane                     Africa/Mogadishu<br>
Africa/Monrovia                    Africa/Nairobi<br>
Africa/Ndjamena                    Africa/Niamey<br>
Africa/Nouakchott                  Africa/Ouagadougou

Africa/Porto-Novo                  Africa/Sao_Tome<br>
Africa/Timbuktu                    Africa/Tripoli<br>
Africa/Tunis                       Africa/Windhoek<br>
America/Adak                       America/Anchorage<br>
America/Anguilla                   America/Antigua<br>
America/Araguaina                  America/Argentina/Buenos_Aires<br>
America/Argentina/Catamarca        America/Argentina/ComodRivadavia<br>
America/Argentina/Cordoba          America/Argentina/Jujuy<br>
America/Argentina/La_Rioja         America/Argentina/Mendoza<br>
America/Argentina/Rio_Gallegos     America/Argentina/Salta<br>
America/Argentina/San_Juan         America/Argentina/San_Luis<br>
America/Argentina/Tucuman          America/Argentina/Ushuaia<br>
America/Aruba                      America/Asuncion<br>
America/Atikokan                   America/Atka<br>
America/Bahia                      America/Bahia_Banderas<br>
America/Barbados                   America/Belem<br>
America/Belize                     America/Blanc-Sablon<br>
America/Boa_Vista                  America/Bogota<br>
America/Boise                      America/Buenos_Aires<br>
America/Cambridge_Bay              America/Campo_Grande<br>
America/Cancun                     America/Caracas<br>
America/Catamarca                  America/Cayenne<br>
America/Cayman                     America/Chicago<br>
America/Chihuahua                  America/Coral_Harbour<br>
America/Cordoba                    America/Costa_Rica

America/Cuiaba                     America/Curacao<br>
America/Danmarkshavn               America/Dawson<br>
America/Dawson_Creek               America/Denver<br>
America/Detroit                    America/Dominica<br>
America/Edmonton                   America/Eirunepe<br>
America/El_Salvador                America/Ensenada<br>
America/Fort_Wayne                 America/Fortaleza<br>
America/Glace_Bay                  America/Godthab<br>
America/Goose_Bay                  America/Grand_Turk<br>
America/Grenada                    America/Guadeloupe<br>
America/Guatemala                  America/Guayaquil<br>
America/Guyana                     America/Halifax<br>
America/Havana                     America/Hermosillo<br>
America/Indiana/Indianapolis       America/Indiana/Knox<br>
America/Indiana/Marengo            America/Indiana/Petersburg<br>
America/Indiana/Tell_City          America/Indiana/Vevay<br>
America/Indiana/Vincennes          America/Indiana/Winamac<br>
America/Indianapolis               America/Inuvik<br>
America/Iqaluit                    America/Jamaica<br>
America/Jujuy                      America/Juneau<br>
America/Kentucky/Louisville        America/Kentucky/Monticello<br>
America/Knox_IN                    America/Kralendijk<br>
America/La_Paz                     America/Lima<br>
America/Los_Angeles                America/Louisville<br>
America/Lower_Princes              America/Maceio

America/Managua                    America/Manaus<br>
America/Marigot                    America/Martinique<br>
America/Matamoros                  America/Mazatlan<br>
America/Mendoza                    America/Menominee<br>
America/Merida                     America/Metlakatla<br>
America/Mexico_City                America/Miquelon<br>
America/Moncton                    America/Monterrey<br>
America/Montevideo                 America/Montreal<br>
America/Montserrat                 America/Nassau<br>
America/New_York                   America/Nipigon<br>
America/Nome                       America/Noronha<br>
America/North_Dakota/Beulah        America/North_Dakota/Center<br>
America/North_Dakota/New_Salem     America/Ojinaga<br>
America/Panama                     America/Pangnirtung<br>
America/Paramaribo                 America/Phoenix<br>
America/Port-au-Prince             America/Port_of_Spain<br>
America/Porto_Acre                 America/Porto_Velho<br>
America/Puerto_Rico                America/Rainy_River<br>
America/Rankin_Inlet               America/Recife<br>
America/Regina                     America/Resolute<br>
America/Rio_Branco                 America/Rosario<br>
America/Santa_Isabel               America/Santarem<br>
America/Santiago                   America/Santo_Domingo<br>
America/Sao_Paulo                  America/Scoresbysund<br>
America/Shiprock                   America/Sitka

America/St_Barthelemy              America/St_Johns<br>
America/St_Kitts                   America/St_Lucia<br>
America/St_Thomas                  America/St_Vincent<br>
America/Swift_Current              America/Tegucigalpa<br>
America/Thule                      America/Thunder_Bay<br>
America/Tijuana                    America/Toronto<br>
America/Tortola                    America/Vancouver<br>
America/Virgin                     America/Whitehorse<br>
America/Winnipeg                   America/Yakutat<br>
America/Yellowknife                Antarctica/Casey<br>
Antarctica/Davis                   Antarctica/DumontDUrville<br>
Antarctica/Macquarie               Antarctica/Mawson<br>
Antarctica/McMurdo                 Antarctica/Palmer<br>
Antarctica/Rothera                 Antarctica/South_Pole<br>
Antarctica/Syowa                   Antarctica/Vostok<br>
Arctic/Longyearbyen                Asia/Aden<br>
Asia/Almaty                        Asia/Amman<br>
Asia/Anadyr                        Asia/Aqtau<br>
Asia/Aqtobe                        Asia/Ashgabat<br>
Asia/Ashkhabad                     Asia/Baghdad<br>
Asia/Bahrain                       Asia/Baku<br>
Asia/Bangkok                       Asia/Beirut<br>
Asia/Bishkek                       Asia/Brunei<br>
Asia/Calcutta                      Asia/Choibalsan<br>
Asia/Chongqing                     Asia/Chungking

Asia/Colombo                       Asia/Dacca<br>
Asia/Damascus                      Asia/Dhaka<br>
Asia/Dili                          Asia/Dubai<br>
Asia/Dushanbe                      Asia/Gaza<br>
Asia/Harbin                        Asia/Hebron<br>
Asia/Ho_Chi_Minh                   Asia/Hong_Kong<br>
Asia/Hovd                          Asia/Irkutsk<br>
Asia/Istanbul                      Asia/Jakarta<br>
Asia/Jayapura                      Asia/Jerusalem<br>
Asia/Kabul                         Asia/Kamchatka<br>
Asia/Karachi                       Asia/Kashgar<br>
Asia/Kathmandu                     Asia/Katmandu<br>
Asia/Kolkata                       Asia/Krasnoyarsk<br>
Asia/Kuala_Lumpur                  Asia/Kuching<br>
Asia/Kuwait                        Asia/Macao<br>
Asia/Macau                         Asia/Magadan<br>
Asia/Makassar                      Asia/Manila<br>
Asia/Muscat                        Asia/Nicosia<br>
Asia/Novokuznetsk                  Asia/Novosibirsk<br>
Asia/Omsk                          Asia/Oral<br>
Asia/Phnom_Penh                    Asia/Pontianak<br>
Asia/Pyongyang                     Asia/Qatar<br>
Asia/Qyzylorda                     Asia/Rangoon<br>
Asia/Riyadh                        Asia/Riyadh87<br>
Asia/Riyadh88                      Asia/Riyadh89

Asia/Saigon                        Asia/Sakhalin<br>
Asia/Samarkand                     Asia/Seoul<br>
Asia/Shanghai                      Asia/Singapore<br>
Asia/Taipei                        Asia/Tashkent<br>
Asia/Tbilisi                       Asia/Tehran<br>
Asia/Tel_Aviv                      Asia/Thimbu<br>
Asia/Thimphu                       Asia/Tokyo<br>
Asia/Ujung_Pandang                 Asia/Ulaanbaatar<br>
Asia/Ulan_Bator                    Asia/Urumqi<br>
Asia/Vientiane                     Asia/Vladivostok<br>
Asia/Yakutsk                       Asia/Yekaterinburg<br>
Asia/Yerevan                       Atlantic/Azores<br>
Atlantic/Bermuda                   Atlantic/Canary<br>
Atlantic/Cape_Verde                Atlantic/Faeroe<br>
Atlantic/Faroe                     Atlantic/Jan_Mayen<br>
Atlantic/Madeira                   Atlantic/Reykjavik<br>
Atlantic/South_Georgia             Atlantic/St_Helena<br>
Atlantic/Stanley                   Australia/ACT<br>
Australia/Adelaide                 Australia/Brisbane<br>
Australia/Broken_Hill              Australia/Canberra<br>
Australia/Currie                   Australia/Darwin<br>
Australia/Eucla                    Australia/Hobart<br>
Australia/LHI                      Australia/Lindeman<br>
Australia/Lord_Howe                Australia/Melbourne<br>
Australia/North                    Australia/NSW

Australia/Perth                    Australia/Queensland<br>
Australia/South                    Australia/Sydney<br>
Australia/Tasmania                 Australia/Victoria<br>
Australia/West                     Australia/Yancowinna<br>
Brazil/Acre                        Brazil/DeNoronha<br>
Brazil/East                        Brazil/West<br>
Canada/Atlantic                    Canada/Central<br>
Canada/East-Saskatchewan           Canada/Eastern<br>
Canada/Mountain                    Canada/Newfoundland<br>
Canada/Pacific                     Canada/Saskatchewan<br>
Canada/Yukon                       CET<br>
Chile/Continental                  Chile/EasterIsland<br>
CST6CDT                            Cuba<br>
EET                                Egypt<br>
Eire                               EST<br>
EST5EDT                            Etc/GMT<br>
Etc/GMT+0                          Etc/GMT+1<br>
Etc/GMT+10                         Etc/GMT+11<br>
Etc/GMT+12                         Etc/GMT+2<br>
Etc/GMT+3                          Etc/GMT+4<br>
Etc/GMT+5                          Etc/GMT+6<br>
Etc/GMT+7                          Etc/GMT+8<br>
Etc/GMT+9                          Etc/GMT-0<br>
Etc/GMT-1                          Etc/GMT-10<br>
Etc/GMT-11                         Etc/GMT-12

Etc/GMT-13                         Etc/GMT-14<br>
Etc/GMT-2                          Etc/GMT-3<br>
Etc/GMT-4                          Etc/GMT-5<br>
Etc/GMT-6                          Etc/GMT-7<br>
Etc/GMT-8                          Etc/GMT-9<br>
Etc/GMT0                           Etc/Greenwich<br>
Etc/UCT                            Etc/Universal<br>
Etc/UTC                            Etc/Zulu<br>
Europe/Amsterdam                   Europe/Andorra<br>
Europe/Athens                      Europe/Belfast<br>
Europe/Belgrade                    Europe/Berlin<br>
Europe/Bratislava                  Europe/Brussels<br>
Europe/Bucharest                   Europe/Budapest<br>
Europe/Chisinau                    Europe/Copenhagen<br>
Europe/Dublin                      Europe/Gibraltar<br>
Europe/Guernsey                    Europe/Helsinki<br>
Europe/Isle_of_Man                 Europe/Istanbul<br>
Europe/Jersey                      Europe/Kaliningrad<br>
Europe/Kiev                        Europe/Lisbon<br>
Europe/Ljubljana                   Europe/London<br>
Europe/Luxembourg                  Europe/Madrid<br>
Europe/Malta                       Europe/Mariehamn<br>
Europe/Minsk                       Europe/Monaco<br>
Europe/Moscow                      Europe/Nicosia<br>
Europe/Oslo                        Europe/Paris

Europe/Podgorica                   Europe/Prague<br>
Europe/Riga                        Europe/Rome<br>
Europe/Samara                      Europe/San_Marino<br>
Europe/Sarajevo                    Europe/Simferopol<br>
Europe/Skopje                      Europe/Sofia<br>
Europe/Stockholm                   Europe/Tallinn<br>
Europe/Tirane                      Europe/Tiraspol<br>
Europe/Uzhgorod                    Europe/Vaduz<br>
Europe/Vatican                     Europe/Vienna<br>
Europe/Vilnius                     Europe/Volgograd<br>
Europe/Warsaw                      Europe/Zagreb<br>
Europe/Zaporozhye                  Europe/Zurich<br>
Factory                            GB<br>
GB-Eire                            GMT<br>
GMT+0                              GMT-0<br>
GMT0                               Greenwich<br>
Hongkong                           HST<br>
Iceland                            Indian/Antananarivo<br>
Indian/Chagos                      Indian/Christmas<br>
Indian/Cocos                       Indian/Comoro<br>
Indian/Kerguelen                   Indian/Mahe<br>
Indian/Maldives                    Indian/Mauritius<br>
Indian/Mayotte                     Indian/Reunion<br>
Iran                               Israel<br>
Jamaica                            Japan

Kwajalein                          Libya<br>
MET                                Mexico/BajaNorte<br>
Mexico/BajaSur                     Mexico/General<br>
Mideast/Riyadh87                   Mideast/Riyadh88<br>
Mideast/Riyadh89                   MST<br>
MST7MDT                            Navajo<br>
NZ                                 NZ-CHAT<br>
Pacific/Apia                       Pacific/Auckland<br>
Pacific/Chatham                    Pacific/Chuuk<br>
Pacific/Easter                     Pacific/Efate<br>
Pacific/Enderbury                  Pacific/Fakaofo<br>
Pacific/Fiji                       Pacific/Funafuti<br>
Pacific/Galapagos                  Pacific/Gambier<br>
Pacific/Guadalcanal                Pacific/Guam<br>
Pacific/Honolulu                   Pacific/Johnston<br>
Pacific/Kiritimati                 Pacific/Kosrae<br>
Pacific/Kwajalein                  Pacific/Majuro<br>
Pacific/Marquesas                  Pacific/Midway<br>
Pacific/Nauru                      Pacific/Niue<br>
Pacific/Norfolk                    Pacific/Noumea<br>
Pacific/Pago_Pago                  Pacific/Palau<br>
Pacific/Pitcairn                   Pacific/Pohnpei<br>
Pacific/Ponape                     Pacific/Port_Moresby<br>
Pacific/Rarotonga                  Pacific/Saipan<br>
Pacific/Samoa                      Pacific/Tahiti

Pacific/Tarawa                     Pacific/Tongatapu<br>
Pacific/Truk                       Pacific/Wake<br>
Pacific/Wallis                     Pacific/Yap<br>
Poland                             Portugal<br>
PRC                                PST8PDT<br>
ROC                                ROK<br>
Singapore                          Turkey<br>
UCT                                Universal<br>
US/Alaska                          US/Aleutian<br>
US/Arizona                         US/Central<br>
US/East-Indiana                    US/Eastern<br>
US/Hawaii                          US/Indiana-Starke<br>
US/Michigan                        US/Mountain<br>
US/Pacific                         US/Pacific-New<br>
US/Samoa                           UTC<br>
W-SU                               WET<br>
Zulu
