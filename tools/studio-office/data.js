// GRABZ STUDIO — данные офиса: отделы, 49 ролей, реплики.
"use strict";

// Отделы: комната (в тайлах), стиль одежды, лид (кто подходит проверять работу)
const DEPARTMENTS = {
  dir:   { name: "Кабинет руководства", short: "Руководство", lead: null, color: "#c9a227" },
  art:   { name: "Арт и левел-дизайн", short: "Арт и дизайн", lead: "art-director", color: "#ff6fae" },
  dev:   { name: "Разработка и сеть", short: "Разработка", lead: "lead-programmer", color: "#4fc3f7" },
  eng:   { name: "Гильдия движков", short: "Движки", lead: "technical-director", color: "#9575cd" },
  audio: { name: "Звук и сценарий", short: "Звук и сценарий", lead: "audio-director", color: "#ffb74d" },
  qa:    { name: "VR QA-лаборатория", short: "QA", lead: "qa-lead", color: "#66bb6a" },
  prod:  { name: "Продакшен и Live-Ops", short: "Продакшен", lead: "producer", color: "#ef5350" },
};

// id → [должность, отдел, задача по умолчанию]
const ROLES = {
  "creative-director":            ["Креативный директор", "dir", "Держит видение GRABZ VR"],
  "technical-director":           ["Технический директор", "dir", "Архитектура и риски"],
  "producer":                     ["Продюсер", "dir", "План спринта и сроки"],

  "art-director":                 ["Арт-директор", "art", "Стиль и CC0-ассеты"],
  "level-designer":               ["Левел-дизайнер", "art", "Планировка банка"],
  "world-builder":                ["Создатель мира", "art", "Лор Майами и банка"],
  "technical-artist":             ["Технический художник", "art", "Шейдеры и оптимизация арта"],
  "ux-designer":                  ["UX-дизайнер", "art", "Флоу GRABZ OS"],
  "accessibility-specialist":     ["Специалист по доступности", "art", "Комфорт в VR"],
  "game-designer":                ["Геймдизайнер", "art", "Механики ограбления"],
  "systems-designer":             ["Дизайнер систем", "art", "Формулы тревоги и лута"],
  "economy-designer":             ["Дизайнер экономики", "art", "Ценность лута"],

  "lead-programmer":              ["Ведущий программист", "dev", "Код-ревью"],
  "unity-specialist":             ["Unity-специалист", "dev", "URP и XR Toolkit"],
  "network-programmer":           ["Сетевой программист", "dev", "Photon Fusion"],
  "gameplay-programmer":          ["Геймплей-программист", "dev", "Хват и передвижение руками"],
  "ai-programmer":                ["AI-программист", "dev", "Охрана и кассирши"],
  "tools-programmer":             ["Программист инструментов", "dev", "Editor-скрипты"],
  "engine-programmer":            ["Программист движка", "dev", "Физика и загрузка сцен"],
  "ui-programmer":                ["UI-программист", "dev", "3D-табло и GRABZ OS"],
  "security-engineer":            ["Инженер безопасности", "dev", "Античит лута"],
  "devops-engineer":              ["DevOps-инженер", "dev", "Сборки Quest и CI"],
  "unity-addressables-specialist":["Addressables-специалист", "dev", "Загрузка ассетов"],
  "unity-dots-specialist":        ["DOTS-специалист", "dev", "ECS и Burst"],
  "unity-shader-specialist":      ["Шейдерный специалист Unity", "dev", "Stencil-портал автобуса"],
  "unity-ui-specialist":          ["Unity UI-специалист", "dev", "World Space UI"],
  "prototyper":                   ["Прототипер", "dev", "Быстрые прототипы"],

  "godot-specialist":             ["Godot-специалист", "eng", "Консультации по Godot"],
  "godot-csharp-specialist":      ["Godot C#-специалист", "eng", "C# в Godot"],
  "godot-gdextension-specialist": ["GDExtension-специалист", "eng", "Нативные модули"],
  "godot-gdscript-specialist":    ["GDScript-специалист", "eng", "Типизированный GDScript"],
  "godot-shader-specialist":      ["Godot-шейдерщик", "eng", "Шейдеры Godot"],
  "unreal-specialist":            ["Unreal-специалист", "eng", "Консультации по Unreal"],
  "ue-blueprint-specialist":      ["Blueprint-специалист", "eng", "Blueprint-архитектура"],
  "ue-gas-specialist":            ["GAS-специалист", "eng", "Gameplay Ability System"],
  "ue-replication-specialist":    ["Специалист по репликации UE", "eng", "Сеть в Unreal"],
  "ue-umg-specialist":            ["UMG-специалист", "eng", "UI в Unreal"],

  "audio-director":               ["Аудио-директор", "audio", "Звуковая палитра"],
  "sound-designer":               ["Саунд-дизайнер", "audio", "Звук лазеров и сирены"],
  "narrative-director":           ["Нарративный директор", "audio", "Сюжет ограбления"],
  "writer":                       ["Сценарист", "audio", "Реплики и таблички"],
  "localization-lead":            ["Лид локализации", "audio", "Русский и английский"],

  "qa-lead":                      ["QA-лид", "qa", "План тестирования"],
  "qa-tester":                    ["QA-тестер", "qa", "Тест билда в шлеме"],
  "performance-analyst":          ["Аналитик производительности", "qa", "72 FPS на Quest"],

  "release-manager":              ["Релиз-менеджер", "prod", "Релиз в Meta Store"],
  "community-manager":            ["Комьюнити-менеджер", "prod", "Патч-ноуты и Discord"],
  "live-ops-designer":            ["Live-Ops дизайнер", "prod", "Сезонные события"],
  "analytics-engineer":           ["Инженер аналитики", "prod", "Телеметрия раундов"],
};

// Кто кому обычно передаёт задачу (для живого общения и демо-режима)
const HANDOFFS = {
  "creative-director": ["art-director", "game-designer", "narrative-director"],
  "technical-director": ["lead-programmer", "network-programmer", "performance-analyst"],
  "producer": ["qa-lead", "release-manager", "creative-director"],
  "art-director": ["level-designer", "technical-artist", "world-builder"],
  "level-designer": ["unity-specialist", "qa-tester", "gameplay-programmer"],
  "world-builder": ["writer", "level-designer"],
  "technical-artist": ["unity-shader-specialist", "performance-analyst"],
  "ux-designer": ["ui-programmer", "unity-ui-specialist"],
  "accessibility-specialist": ["ux-designer", "qa-tester"],
  "game-designer": ["systems-designer", "gameplay-programmer", "level-designer"],
  "systems-designer": ["economy-designer", "gameplay-programmer"],
  "economy-designer": ["game-designer", "analytics-engineer"],
  "lead-programmer": ["gameplay-programmer", "network-programmer", "tools-programmer"],
  "unity-specialist": ["qa-tester", "lead-programmer"],
  "network-programmer": ["qa-tester", "security-engineer"],
  "gameplay-programmer": ["qa-tester", "lead-programmer"],
  "ai-programmer": ["qa-tester", "game-designer"],
  "tools-programmer": ["level-designer", "technical-artist"],
  "engine-programmer": ["performance-analyst", "lead-programmer"],
  "ui-programmer": ["ux-designer", "qa-tester"],
  "security-engineer": ["network-programmer", "lead-programmer"],
  "devops-engineer": ["release-manager", "qa-lead"],
  "unity-addressables-specialist": ["devops-engineer", "performance-analyst"],
  "unity-dots-specialist": ["performance-analyst", "engine-programmer"],
  "unity-shader-specialist": ["technical-artist", "performance-analyst"],
  "unity-ui-specialist": ["ui-programmer", "qa-tester"],
  "prototyper": ["game-designer", "creative-director"],
  "godot-specialist": ["technical-director"], "godot-csharp-specialist": ["godot-specialist"],
  "godot-gdextension-specialist": ["godot-specialist"], "godot-gdscript-specialist": ["godot-specialist"],
  "godot-shader-specialist": ["godot-specialist"], "unreal-specialist": ["technical-director"],
  "ue-blueprint-specialist": ["unreal-specialist"], "ue-gas-specialist": ["unreal-specialist"],
  "ue-replication-specialist": ["unreal-specialist"], "ue-umg-specialist": ["unreal-specialist"],
  "audio-director": ["sound-designer", "creative-director"],
  "sound-designer": ["unity-specialist", "qa-tester"],
  "narrative-director": ["writer", "world-builder"],
  "writer": ["localization-lead", "narrative-director"],
  "localization-lead": ["qa-tester", "ui-programmer"],
  "qa-lead": ["qa-tester", "producer"],
  "qa-tester": ["qa-lead", "lead-programmer", "gameplay-programmer"],
  "performance-analyst": ["technical-director", "technical-artist"],
  "release-manager": ["producer", "community-manager"],
  "community-manager": ["producer", "live-ops-designer"],
  "live-ops-designer": ["economy-designer", "analytics-engineer"],
  "analytics-engineer": ["live-ops-designer", "producer"],
};

// Реплики при передаче задачи: [от кого, кому] по отделу отправителя
const HANDOFF_LINES = {
  dir:   [["План утверждён, {to} — забирай!", "Принял, приступаю!"], ["Ставлю приоритет: сначала VR-комфорт.", "Понял, перестраиваю задачи."]],
  art:   [["Нашёл CC0-ассеты для банка, передаю {to}!", "Принял, собираю геометрию и коллайдеры!"],
          ["Палитра Майами утверждена, глянь, {to}.", "Шикарный закат! Беру в работу."],
          ["Планировка подвала готова, {to}, проверь проходы.", "Прохожу маршрут к хранилищу..."]],
  dev:   [["Пуш в main! {to}, проверь билд.", "Проверяю билд в VR-шлеме..."],
          ["Синк лута через Fusion готов, {to}.", "Отлично, гоняю двух клиентов!"],
          ["Отрефакторил хват руками, {to}, глянь PR.", "Смотрю диф, пара замечаний будет."]],
  eng:   [["По Godot всё изучил, {to}, держи заметки.", "Спасибо, учтём в архитектуре."],
          ["Сравнил репликацию с Fusion, {to}.", "Полезно! Оставляем Fusion."]],
  audio: [["Звук сирены готов, {to}, вставишь?", "Вешаю на AlarmSystem!"],
          ["Новые реплики для табло, {to}.", "Перевожу и проверяю длину строк."]],
  qa:    [["Нашёл баг: рука застревает в стене! {to}", "Ох. Чиню коллайдер!"],
          ["72 FPS на Quest держится, {to}.", "Отлично, идём к релизу!"],
          ["Регресс пройден, {to}, всё зелёное.", "Принято, отмечаю в спринте."]],
  prod:  [["Патч-ноуты готовы, {to}, утверди.", "Утверждаю, публикуем!"],
          ["Метрики раундов выросли на 12%, {to}.", "Супер, планируем ивент!"]],
};

const START_LINES = ["Погнали!", "Беру задачу.", "Так, где мой кофе...", "Открываю проект...", "Сейчас сделаем красиво!", "Читаю CLAUDE.md..."];
const LEAD_LINES = ["Хорошо идёт!", "Не забудь про Quest.", "Глянул — одобряю 👍", "Только без нейрослопа!", "Добавь тест, плиз.", "Красота!"];

// Облачка мыслей в свободное время
const IDLE_THOUGHTS = [
  "А если гориллы будут грабить казино?", "Нужно больше лазеров...", "Кофе — это VR для мозга.",
  "Интересно, кто съел мой донат?", "Лут в руках должен быть тяжелее!", "Когда уже пятница?",
  "Банк SUNSET TRUST — топ!", "Надо проверить коллайдеры...", "Горилла в маске — гениально.",
  "Хочу золотую статую в хаб.", "Fusion или не Fusion?", "72 FPS или смерть!",
  "А фургон можно перекрасить?", "Сыграем в настольный футбол?", "Где мой VR-шлем?",
  "Нейрослоп — нет. Арт — да!", "Сирена слишком громкая?", "Гориллы не умеют прыгать... или умеют?",
];
const CHAT_PAIRS = [
  ["Видел новый фургон?", "Чёрный автобус — огонь!"], ["Как билд?", "Летает на Quest!"],
  ["Кто сломал main?", "Не я! ...наверное."], ["Пошли в VR-лабу?", "Только допью кофе."],
  ["Что на обед?", "Бананы, конечно 🍌"], ["Сколько лута вынесли?", "Рекорд — $9000!"],
  ["Новая маска грабителя?", "Да, по форме лица!"], ["Лазеры успели починить?", "Да, теперь у всех синхронно."],
];
const VR_LINES = ["Проверяю билд в VR-шлеме...", "Хват работает!", "Машу руками — летим!", "Ой, стена!", "Лазеры видно чётко."];
