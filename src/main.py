import sys
import os
import requests
import webbrowser
import configparser
import subprocess
import threading
import time
import ctypes
from ctypes import wintypes
import tempfile
import shutil
from datetime import datetime, timedelta, timezone

from PyQt6.QtWidgets import (
    QApplication, QMainWindow, QWidget, QVBoxLayout, QHBoxLayout,
    QLabel, QScrollArea, QCheckBox, QFrame, QGraphicsDropShadowEffect, 
    QSizePolicy, QTabWidget, QSystemTrayIcon, QMenu, QGraphicsOpacityEffect, QDialog,
    QLineEdit, QPushButton, QComboBox
)
from PyQt6.QtCore import Qt, QTimer, QThread, pyqtSignal, QRect, QPropertyAnimation, QEasingCurve, QPoint, QUrl, QSize, QObject
from PyQt6.QtGui import QColor, QScreen, QIcon, QAction, QDesktopServices, QIntValidator, QPixmap, QKeySequence
from PyQt6.QtMultimedia import QMediaPlayer, QAudioOutput 

user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

class MSG(ctypes.Structure):
    _fields_ = [("hwnd", ctypes.c_void_p),
                ("message", ctypes.c_uint),
                ("wParam", ctypes.c_void_p),
                ("lParam", ctypes.c_void_p),
                ("time", ctypes.c_ulong),
                ("pt", wintypes.POINT)]

WM_HOTKEY = 0x0312
MOD_NOREPEAT = 0x4000
MOD_CONTROL = 0x0002
MOD_SHIFT = 0x0004
MOD_ALT = 0x0001
WM_QUIT = 0x0012

def resource_path(relative_path):
    try:
        base_path = sys._MEIPASS
    except Exception:
        base_path = os.path.dirname(os.path.abspath(__file__))
    return os.path.join(base_path, relative_path)

APP_NAME = "ARC-Sight"
CONFIG_DIR = os.path.join(os.getenv('APPDATA') or os.path.expanduser('~'), APP_NAME)
CONFIG_FILE = os.path.join(CONFIG_DIR, 'config.ini')
ASSETS_DIR = resource_path('assets')
LANG_DIR = resource_path('languages')
SOUND_FILE = resource_path(os.path.join('assets', 'Notif.wav'))

DEFAULT_CONFIG = {
    'hotkey': 'F9',
    'notify_minutes': 5,
    'sound_enabled': 'True',
    'language': 'en',
    'show_local_time': 'False'
}

API_URL = "https://metaforge.app/api/arc-raiders/event-timers"
REFRESH_API_INTERVAL_SEC = 60 
REFRESH_UI_INTERVAL_MS = 500

MASTER_SERVER_GITHUB_URL = "https://arc-sight-stats-viewer.onrender.com/ping"
GITHUB_RELEASE_API = "https://api.github.com/repos/rodafux/ARC-Sight/releases/latest"
GITHUB_RELEASE_PAGE = "https://github.com/rodafux/ARC-Sight/releases"
IN_OVERLAY_MSG_URL = "https://raw.githubusercontent.com/rodafux/ARC-Sight/Release/src/in_overlay_msg.ini"

HOTKEY = DEFAULT_CONFIG['hotkey']
NOTIFY_SECONDS = DEFAULT_CONFIG['notify_minutes'] * 60
SOUND_ENABLED = DEFAULT_CONFIG['sound_enabled'] == 'True'
CURRENT_LANGUAGE = DEFAULT_CONFIG['language']
TRANSLATION_STRINGS = {} 
AUTO_CLOSE = False
GAME_WINDOW_TITLE = "ARC Raiders" 
BANNER_HEIGHT = 350 
CARD_WIDTH = 160
CARD_HEIGHT = 190

MAP_IMAGES = {
    "Dam": "Barrage.png", "Spaceport": "Port_spatial.png", "Buried City": "Ville_enfouie.png", 
    "Blue Gate": "Portail_bleu.png", "Stella Montis": "Stella_montis.png"
}

HUD_STYLESHEET = """
QMainWindow { background-color: rgba(12, 12, 12, 210); }
QTabWidget::pane { border: none; background: transparent; }
QTabWidget::tab-bar { alignment: center; }
QTabBar::tab {
    background: transparent; color: #ffffff; padding: 8px 20px;
    margin-top: 80px; margin-bottom: 5px; margin-left: 5px; margin-right: 5px;
    font-family: 'Segoe UI', sans-serif; font-weight: 800; font-size: 11px; text-transform: uppercase; letter-spacing: 1px; border-bottom: 2px solid transparent;
}
QTabBar::tab:selected { color: #ff5500; border-bottom: 2px solid #ff5500; background: rgba(255, 85, 0, 0.08); }
QTabBar::tab:hover { color: #ddd; background: rgba(255, 255, 255, 0.05); }
QScrollArea { border: none; background: transparent; }
QWidget#ScrollContent { background: transparent; }
QScrollBar:horizontal { border: none; background: rgba(30, 30, 30, 150); height: 4px; margin-top: 5px;}
QScrollBar::handle:horizontal { background: #ff5500; min-width: 20px; border-radius: 2px; }
QScrollBar::add-line:horizontal, QScrollBar::sub-line:horizontal { width: 0px; }
QFrame#HudCard { background-color: rgba(25, 25, 25, 255); border-radius: 4px; border: 1px solid #333; }
QWidget#CardOverlay { background-color: rgba(0, 0, 0, 0.75); border-radius: 4px; }
QLabel#EventCategory { font-family: 'Segoe UI', sans-serif; font-size: 10px; font-weight: 900; color: #00e5ff; text-transform: uppercase; letter-spacing: 1px; margin-bottom: 2px; }
QLabel#MapName { font-family: 'Segoe UI', sans-serif; font-weight: 600; font-size: 12px; color: #eeeeee; text-transform: uppercase; }
QLabel#TimerLabel { font-family: 'Consolas', monospace; font-weight: 900; font-size: 28px; color: #fff; }
QLabel#StatusLabel { font-size: 9px; color: #888; text-transform: uppercase; font-weight: bold; }
QCheckBox { color: #888; font-size: 9px; spacing: 5px; text-transform: uppercase; font-weight: bold;}
QCheckBox::indicator { width: 10px; height: 10px; border: 1px solid #555; background: #222; border-radius: 2px; }
QCheckBox::indicator:checked { background-color: #ff5500; border-color: #ff5500; }
QDialog { background-color: #1a1a1a; }
QDialog QLineEdit, QDialog QComboBox { background-color: #222; border: 1px solid #444; padding: 5px; border-radius: 3px; color: #eee; }
QPushButton { background-color: #ff5500; color: black; font-weight: bold; padding: 8px; border: none; border-radius: 3px; }
QPushButton:hover { background-color: #ff7733; }
QCheckBox.SettingsCB { color: #ffffff; padding: 5px; }
QCheckBox.SettingsCB::indicator { width: 14px; height: 14px; border: 1px solid #555; border-radius: 3px; background: #222; }
QCheckBox::indicator:checked { background-color: #00e5ff; border-color: #00e5ff; }
"""

def get_translation(key, section='UI'):
    if not key: return ""
    key_clean = key.replace(' ', '_').replace('-', '_').lower()
    if section in TRANSLATION_STRINGS and key_clean in TRANSLATION_STRINGS[section]:
        return TRANSLATION_STRINGS[section][key_clean]
    return key.upper()

def load_language(lang_code):
    global TRANSLATION_STRINGS
    filename = f'lang_{lang_code.lower()}.ini'
    lang_file = os.path.join(LANG_DIR, filename)
    config = configparser.ConfigParser(interpolation=None)
    if not os.path.exists(lang_file):
        lang_file = os.path.join(LANG_DIR, 'lang_fr.ini')
        if not os.path.exists(lang_file):
            TRANSLATION_STRINGS = {} 
            return
    try:
        read_files = config.read(lang_file, encoding='utf-8-sig')
        if not read_files: raise Exception("Fichier vide")
        TRANSLATION_STRINGS = {section: dict(config.items(section)) for section in config.sections()}
    except Exception:
        TRANSLATION_STRINGS = {}

def load_config():
    config = configparser.ConfigParser()
    if not os.path.exists(CONFIG_FILE):
        config['Settings'] = DEFAULT_CONFIG
        save_config(config)
    config.read(CONFIG_FILE)
    global HOTKEY, NOTIFY_SECONDS, SOUND_ENABLED, CURRENT_LANGUAGE, SHOW_LOCAL_TIME
    try:
        HOTKEY = config.get('Settings', 'hotkey', fallback=DEFAULT_CONFIG['hotkey'])
        notify_minutes = config.getint('Settings', 'notify_minutes', fallback=DEFAULT_CONFIG['notify_minutes'])
        SOUND_ENABLED = config.getboolean('Settings', 'sound_enabled', fallback=DEFAULT_CONFIG['sound_enabled']=='True') 
        CURRENT_LANGUAGE = config.get('Settings', 'language', fallback=DEFAULT_CONFIG['language'])
        SHOW_LOCAL_TIME = config.getboolean('Settings', 'show_local_time', fallback=False)
        NOTIFY_SECONDS = notify_minutes * 60
    except configparser.Error:
        HOTKEY = DEFAULT_CONFIG['hotkey']
        NOTIFY_SECONDS = DEFAULT_CONFIG['notify_minutes'] * 60
        SOUND_ENABLED = DEFAULT_CONFIG['sound_enabled'] == 'True'
        CURRENT_LANGUAGE = DEFAULT_CONFIG['language']
        SHOW_LOCAL_TIME = False
    load_language(CURRENT_LANGUAGE)
    return config

def save_config(config_parser):
    os.makedirs(CONFIG_DIR, exist_ok=True)
    try:
        with open(CONFIG_FILE, 'w') as configfile:
            config_parser.write(configfile)
    except Exception: pass

class ApiWorker(QThread):
    data_fetched = pyqtSignal(list)
    error_occurred = pyqtSignal(str)
    def run(self):
        try:
            response = requests.get(API_URL, timeout=10)
            if response.status_code != 200: print(f"⚠️ Erreur Serveur Events : {response.status_code}")
            response.raise_for_status()
            data = response.json()
            if isinstance(data, dict) and 'data' in data: self.data_fetched.emit(data['data'])
            elif isinstance(data, list): self.data_fetched.emit(data)
            else: self.data_fetched.emit([])
        except Exception as e:
            print(f"❌ Erreur Events : {e}")
            self.error_occurred.emit(str(e))

class GitHubVersionWorker(QThread):
    version_checked = pyqtSignal(str, str, str) 

    def run(self):
        try:
            headers = {'User-Agent': APP_NAME}
            response = requests.get(GITHUB_RELEASE_API, headers=headers, timeout=10)
            response.raise_for_status()
            data = response.json()
            
            latest_tag = data.get('tag_name', '').lstrip('vV') 
            release_url = data.get('html_url', GITHUB_RELEASE_PAGE)
            
            exe_download_url = ""
            if 'assets' in data:
                for asset in data['assets']:
                    if asset['name'].endswith(".exe"):
                        exe_download_url = asset['browser_download_url']
                        break
            
            if latest_tag: 
                self.version_checked.emit(latest_tag, release_url, exe_download_url)
            else: 
                self.version_checked.emit("", "", "")
                
        except Exception as e:
            print(f"❌ Erreur GitHub API: {e}")
            self.version_checked.emit("", "", "")

class HeartbeatWorker(QThread):
    def __init__(self, app_version):
        super().__init__()
        self.app_version = app_version

    def run(self):
        target_url = "https://arc-sight-stats-viewer.onrender.com/ping"
        headers = {"User-Agent": "ARC-Sight-Desktop-Client/1.0", "X-App-Secret": "ARC-RAIDERS-OPS"}
        while True:
            try:
                requests.post(target_url, timeout=60, headers=headers, json={"version": self.app_version})
            except Exception as e: 
                print(f"Erreur Heartbeat: {e}")
            self.sleep(60)

class MessageWorker(QThread):
    message_found = pyqtSignal(str)

    def run(self):
        try:
            url_with_cache_bust = f"{IN_OVERLAY_MSG_URL}?t={int(time.time())}"
            response = requests.get(url_with_cache_bust, timeout=5)
            response.raise_for_status()
            
            raw_content = response.text
            ini_str = f"[MESSAGES]\n{raw_content}"
            
            config = configparser.ConfigParser()
            config.read_string(ini_str)
            
            lang_key = CURRENT_LANGUAGE.upper()
            message = ""
            
            if 'MESSAGES' in config:
                if lang_key in config['MESSAGES']:
                    message = config['MESSAGES'][lang_key]
                elif 'EN' in config['MESSAGES']:
                    message = config['MESSAGES']['EN']
            
            message = message.strip('"').strip("'")
            
            if not message:
                self.message_found.emit("")
            else:
                self.message_found.emit(message)
            
        except Exception as e:
            print(f"Erreur Message Banner: {e}")
            self.message_found.emit("")

class NativeHotKey(QThread):
    trigger = pyqtSignal()

    def __init__(self, key_code=None, modifiers=0):
        super().__init__()
        self.key_code = key_code
        self.modifiers = modifiers
        self.hk_id = 1
        self.thread_id = None 

    def run(self):
        if not self.key_code: return
        self.thread_id = kernel32.GetCurrentThreadId()
        if not user32.RegisterHotKey(None, self.hk_id, self.modifiers | MOD_NOREPEAT, self.key_code):
            return
        msg = MSG()
        try:
            while user32.GetMessageW(ctypes.byref(msg), None, 0, 0) > 0:
                if msg.message == WM_HOTKEY:
                    self.trigger.emit()
                elif msg.message == WM_QUIT:
                    break 
                user32.TranslateMessage(ctypes.byref(msg))
                user32.DispatchMessageW(ctypes.byref(msg))
        finally:
            user32.UnregisterHotKey(None, self.hk_id)

    def stop(self):
        if self.thread_id:
            user32.PostThreadMessageW(self.thread_id, WM_QUIT, 0, 0)
        self.quit()
        self.wait()

def get_vk_code(text):
    text = text.upper()
    if text.startswith('F'):
        try:
            num = int(text[1:])
            if 1 <= num <= 24:
                return 0x70 + (num - 1)
        except ValueError: pass
    if len(text) == 1 and text.isdigit():
        return 0x30 + int(text)
    if len(text) == 1 and text.isalpha():
        return 0x41 + (ord(text) - ord('A'))
    return 0x78

class AudioPlayer(QObject):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.player = QMediaPlayer()
        self.audio_output = QAudioOutput()
        self.player.setAudioOutput(self.audio_output)
    def play_sound(self, file_path):
        if not os.path.exists(file_path): return
        self.player.setSource(QUrl.fromLocalFile(file_path))
        self.player.play()

class UpdateDownloader(QThread):
    progress = pyqtSignal(int)
    finished = pyqtSignal(str)
    error = pyqtSignal(str)

    def __init__(self, download_url):
        super().__init__()
        self.download_url = download_url

    def run(self):
        try:
            temp_dir = tempfile.gettempdir()
            installer_name = f"ARC-Sight-Setup.exe"
            save_path = os.path.join(temp_dir, installer_name)
            response = requests.get(self.download_url, stream=True, timeout=30)
            response.raise_for_status()
            total_size = int(response.headers.get('content-length', 0))
            block_size = 8192
            downloaded = 0
            with open(save_path, 'wb') as f:
                for chunk in response.iter_content(chunk_size=block_size):
                    if chunk:
                        f.write(chunk)
                        downloaded += len(chunk)
                        if total_size > 0:
                            percent = int((downloaded / total_size) * 100)
                            self.progress.emit(percent)
            self.finished.emit(save_path)
        except Exception as e:
            self.error.emit(str(e))

class ToastNotification(QWidget):
    def __init__(self, title, message, parent=None):
        super().__init__(parent)
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint | Qt.WindowType.Tool)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setFixedSize(350, 80)
        layout = QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        self.frame = QFrame()
        self.frame.setStyleSheet("QFrame { background-color: rgba(10, 10, 10, 240); border: 2px solid #ff5500; border-radius: 6px; }")
        frame_layout = QVBoxLayout(self.frame)
        title_container = QHBoxLayout()
        lbl_title = QLabel(title)
        lbl_title.setStyleSheet("color: #ff5500; font-weight: bold; font-size: 14px; text-transform: uppercase;")
        lbl_close = QLabel("X")
        lbl_close.setStyleSheet("color: #ff5500; font-size: 16px; font-weight: bold;")
        lbl_close.setCursor(Qt.CursorShape.PointingHandCursor)
        lbl_close.mousePressEvent = self.close_on_click 
        self.frame.mousePressEvent = self.close_on_click 
        title_container.addWidget(lbl_title)
        title_container.addStretch()
        title_container.addWidget(lbl_close)
        lbl_msg = QLabel(message)
        lbl_msg.setStyleSheet("color: white; font-size: 12px;")
        frame_layout.addLayout(title_container)
        frame_layout.addWidget(lbl_msg)
        layout.addWidget(self.frame)
        self.opacity_effect = QGraphicsOpacityEffect(self)
        self.setGraphicsEffect(self.opacity_effect)
        self.timer = QTimer()
        self.timer.setSingleShot(True)
        self.timer.timeout.connect(self.fade_out)
    def close_on_click(self, event): self.fade_out()
    def show_toast(self):
        screen = QApplication.primaryScreen().geometry()
        self.move(screen.width() - self.width() - 20, 20)
        self.show()
        self.fade_in()
    def fade_in(self):
        self.anim = QPropertyAnimation(self.opacity_effect, b"opacity")
        self.anim.setDuration(500)
        self.anim.setStartValue(0)
        self.anim.setEndValue(1)
        self.anim.start()
        self.timer.start(15000) 
    def fade_out(self):
        self.anim = QPropertyAnimation(self.opacity_effect, b"opacity")
        self.anim.setDuration(500)
        self.anim.setStartValue(1)
        self.anim.setEndValue(0)
        self.anim.finished.connect(self.close)
        self.anim.start()

class SettingsWindow(QDialog):
    def __init__(self, main_window, current_hotkey, current_notify_min, current_app_version):
        super().__init__(main_window)
        self.setWindowTitle(get_translation('header', 'SETTINGS'))
        logo_path = resource_path("assets/logo.png")
        if os.path.exists(logo_path): self.setWindowIcon(QIcon(logo_path))
        self.setStyleSheet(HUD_STYLESHEET)
        self.setFixedSize(600, 500) 
        self.main_window = main_window
        self.temp_hotkey_string = current_hotkey
        self.available_languages = self.find_available_languages()
        self.capturing_key = False 
        self.current_app_version = current_app_version
        self.init_ui(current_notify_min)

    def find_available_languages(self):
        langs = {}
        if os.path.exists(LANG_DIR):
            for filename in os.listdir(LANG_DIR):
                if filename.startswith('lang_') and filename.endswith('.ini'):
                    lang_code = filename[5:-4].lower()
                    config = configparser.ConfigParser()
                    config.read(os.path.join(LANG_DIR, filename), encoding='utf-8')
                    lang_name = config.get('META', 'language_name', fallback=lang_code.upper())
                    langs[lang_code] = lang_name
        return langs
    
    def init_ui(self, current_notify_min):
        main_layout = QVBoxLayout(self)
        main_layout.setSpacing(15)
        
        settings_group = QFrame()
        settings_layout = QVBoxLayout(settings_group)
        settings_layout.addWidget(QLabel(f"<h2>{get_translation('header', 'SETTINGS')}</h2>"))
        
        l_box = QHBoxLayout()
        l_box.addWidget(QLabel("Langue / Language:"))
        self.lang_selector = QComboBox()
        for code, name in self.available_languages.items():
            self.lang_selector.addItem(name, code)
            if code == CURRENT_LANGUAGE: self.lang_selector.setCurrentText(name)
        l_box.addWidget(self.lang_selector)
        settings_layout.addLayout(l_box)
        
        n_box = QHBoxLayout()
        n_box.addWidget(QLabel(get_translation('alert_minutes_label', 'SETTINGS')))
        self.notify_input = QLineEdit(str(current_notify_min))
        self.notify_input.setValidator(QIntValidator(1, 60))
        n_box.addWidget(self.notify_input)
        settings_layout.addLayout(n_box)
        
        h_box = QHBoxLayout()
        h_box.addWidget(QLabel(get_translation('hotkey_label', 'SETTINGS')))
        self.hotkey_input = QLineEdit(self.temp_hotkey_string)
        self.hotkey_input.setReadOnly(True) 
        self.hotkey_input.setPlaceholderText("Click to set...")
        self.hotkey_input.mousePressEvent = self.start_key_capture
        h_box.addWidget(self.hotkey_input)
        settings_layout.addLayout(h_box)

        self.time_cb = QCheckBox(get_translation('show_local_time', 'SETTINGS'))
        if self.time_cb.text() == 'SHOW_LOCAL_TIME': self.time_cb.setText("Afficher l'heure locale")
        self.time_cb.setObjectName("SettingsCB")
        self.time_cb.setChecked(SHOW_LOCAL_TIME)
        settings_layout.addWidget(self.time_cb)
        
        self.sound_cb = QCheckBox(get_translation('sound_toggle', 'SETTINGS'))
        self.sound_cb.setObjectName("SettingsCB") 
        self.sound_cb.setChecked(SOUND_ENABLED)
        settings_layout.addWidget(self.sound_cb)

        main_layout.addWidget(settings_group)
        main_layout.addStretch(1)

        about_group = QFrame()
        about_group.setStyleSheet("background-color: rgba(255, 255, 255, 0.05); border-radius: 5px;")
        about_layout = QHBoxLayout(about_group)
        
        text_container = QWidget()
        text_layout = QVBoxLayout(text_container)
        text_layout.setContentsMargins(0, 0, 0, 0)
        text_layout.addWidget(QLabel(f"<h3>{get_translation('about_header', 'SETTINGS')}</h3>"))
        
        try:
            version_txt = get_translation('current_version', 'SETTINGS').format(version=self.current_app_version)
        except: 
            version_txt = f"Version: <b>{self.current_app_version}</b>"
            
        lbl_ver = QLabel(version_txt)
        lbl_ver.setStyleSheet("color: #888;")
        text_layout.addWidget(lbl_ver)
        
        try:
            author_txt = get_translation('created_by', 'SETTINGS').format(author='rodafux')
        except: author_txt = "Created by rodafux"
        text_layout.addWidget(QLabel(author_txt))
        
        html_link = '<a href="https://metaforge.app/arc-raiders" style="color: #ff5500; text-decoration: none;">metaforge.app</a>'
        try:
            api_txt = get_translation('api_source_label', 'SETTINGS').format(api_link=html_link)
        except: api_txt = f"API: {html_link}"
        
        api_label = QLabel(api_txt)
        api_label.setOpenExternalLinks(True)
        text_layout.addWidget(api_label)
        text_layout.addStretch()
        
        logo_label = QLabel()
        logo_path = resource_path("assets/logo.png")
        if os.path.exists(logo_path):
            pixmap = QPixmap(logo_path)
            scaled_pixmap = pixmap.scaled(80, 80, Qt.AspectRatioMode.KeepAspectRatio, Qt.TransformationMode.SmoothTransformation)
            logo_label.setPixmap(scaled_pixmap)
            logo_label.setAlignment(Qt.AlignmentFlag.AlignTop | Qt.AlignmentFlag.AlignRight)
        
        about_layout.addWidget(text_container)
        about_layout.addWidget(logo_label)
        
        main_layout.addWidget(about_group)

        save_btn = QPushButton(get_translation('save_button', 'SETTINGS'))
        save_btn.clicked.connect(self.save_and_apply)
        save_btn.setCursor(Qt.CursorShape.PointingHandCursor)
        main_layout.addWidget(save_btn)

    def start_key_capture(self, event):
        self.capturing_key = True
        self.hotkey_input.setText(get_translation('capture_button_text', 'SETTINGS'))
        self.hotkey_input.setStyleSheet("background-color: #550000; border: 1px solid #ff5500;")
        self.hotkey_input.setFocus() 

    def keyPressEvent(self, event):
        if self.capturing_key:
            key = event.key()
            if key in (Qt.Key.Key_Control, Qt.Key.Key_Shift, Qt.Key.Key_Alt, Qt.Key.Key_Meta):
                return
            sequence = QKeySequence(key)
            key_text = sequence.toString()
            if key_text:
                self.temp_hotkey_string = key_text
                self.hotkey_input.setText(key_text.upper())
                self.hotkey_input.setStyleSheet("background-color: #222; border: 1px solid #444;")
                self.capturing_key = False
            return
        super().keyPressEvent(event)

    def save_and_apply(self):
                try:
                    notify_min = int(self.notify_input.text())
                    if not 1 <= notify_min <= 60: raise ValueError
                except: return
                global HOTKEY, NOTIFY_SECONDS, SOUND_ENABLED, CURRENT_LANGUAGE, SHOW_LOCAL_TIME
                HOTKEY = self.temp_hotkey_string
                NOTIFY_SECONDS = notify_min * 60
                SOUND_ENABLED = self.sound_cb.isChecked()
                SHOW_LOCAL_TIME = self.time_cb.isChecked()
                new_lang = self.lang_selector.currentData()
                lang_changed = (new_lang != CURRENT_LANGUAGE)
                CURRENT_LANGUAGE = new_lang
                config = configparser.ConfigParser()
                config['Settings'] = {
                    'hotkey': HOTKEY,
                    'notify_minutes': str(notify_min),
                    'sound_enabled': str(SOUND_ENABLED),
                    'language': CURRENT_LANGUAGE,
                    'show_local_time': str(SHOW_LOCAL_TIME)
                }
                save_config(config)
                if lang_changed: load_language(CURRENT_LANGUAGE)
                self.accept()
                if self.main_window: self.main_window.apply_configuration()

def get_next_start_timestamp(event_data):
    times = event_data.get('times', [])
    if not times: return datetime.max.replace(tzinfo=timezone.utc)
    now = datetime.now(timezone.utc)
    candidates = []
    for slot in times:
        try:
            sh, sm = map(int, slot['start'].split(':'))
            eh, em = map(int, slot['end'].split(':'))
            if sh == 24: sh = 0
            if eh == 24: eh = 0
            t_start = now.replace(hour=sh, minute=sm, second=0, microsecond=0)
            t_end = now.replace(hour=eh, minute=em, second=0, microsecond=0)
            if t_end <= t_start: t_end += timedelta(days=1)
            if t_start < now:
                if now < t_end: return t_start 
                t_start += timedelta(days=1)
            candidates.append(t_start)
        except: continue
    return min(candidates) if candidates else datetime.max.replace(tzinfo=timezone.utc)

class HudEventCard(QFrame):
    state_changed = pyqtSignal(str, str, bool)

    def __init__(self, event_data, parent=None):
        super().__init__(parent)
        self.setObjectName("HudCard")
        self.event_data = event_data
        self.setFixedSize(CARD_WIDTH, CARD_HEIGHT)
        self.target_time = None
        self.is_active = False
        self.notified_state = False
        self.last_style_state = None
        self.flash_toggle = False
        self.init_ui()
        self.setup_background()
        self.recalculate_schedule()
        self.notify_cb.toggled.connect(self.on_toggle)

    def on_toggle(self, checked):
        name = self.event_data.get('name')
        map_name = self.event_data.get('map')
        self.state_changed.emit(name, map_name, checked)

    def setup_background(self):
        map_name_api = self.event_data.get('map')
        image_file = MAP_IMAGES.get(map_name_api)
        if image_file:
            img_path = resource_path(os.path.join('assets', image_file)).replace("\\", "/")
            if os.path.exists(img_path):
                self.setStyleSheet(f"QFrame#HudCard {{ border-image: url({img_path}) 0 0 0 0 stretch stretch; border: 1px solid #444; border-radius: 4px; }}")

    def init_ui(self):
            main_layout = QVBoxLayout(self)
            main_layout.setContentsMargins(0, 0, 0, 0)
            self.overlay = QWidget()
            self.overlay.setObjectName("CardOverlay")
            overlay_layout = QVBoxLayout(self.overlay)
            overlay_layout.setContentsMargins(10, 12, 10, 12)
            cat_api = self.event_data.get('name', '')
            self.cat_label = QLabel(get_translation(cat_api, 'TABS'))
            self.cat_label.setObjectName("EventCategory")
            self.cat_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            self.cat_label.setWordWrap(True)
            self.timer_label = QLabel("--:--")
            self.timer_label.setObjectName("TimerLabel")
            self.timer_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            map_api = self.event_data.get('map', '')
            self.map_label = QLabel(get_translation(map_api, 'MAPS'))
            self.map_label.setObjectName("MapName")
            self.map_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            self.map_label.setWordWrap(True)
            self.local_start_label = QLabel("")
            self.local_start_label.setStyleSheet("color: #aaaaaa; font-size: 11px; font-weight: 600; margin-top: 2px;")
            self.local_start_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
            self.local_start_label.setVisible(SHOW_LOCAL_TIME)
            bottom_layout = QHBoxLayout()
            self.status_label = QLabel(get_translation('status_unknown', 'UI'))
            self.status_label.setObjectName("StatusLabel")
            self.notify_cb = QCheckBox(get_translation('alert_button_label', 'UI'))
            self.notify_cb.setCursor(Qt.CursorShape.PointingHandCursor)
            self.notify_cb.setLayoutDirection(Qt.LayoutDirection.RightToLeft)
            bottom_layout.addWidget(self.status_label)
            bottom_layout.addStretch()
            bottom_layout.addWidget(self.notify_cb)
            overlay_layout.addWidget(self.cat_label)
            overlay_layout.addStretch(1)
            overlay_layout.addWidget(self.timer_label)
            overlay_layout.addWidget(self.map_label)
            overlay_layout.addWidget(self.local_start_label)
            overlay_layout.addStretch(1)
            overlay_layout.addLayout(bottom_layout)
            main_layout.addWidget(self.overlay)
            shadow = QGraphicsDropShadowEffect()
            shadow.setBlurRadius(15)
            shadow.setColor(QColor(0, 0, 0, 200))
            shadow.setOffset(0, 4)
            self.setGraphicsEffect(shadow)

    def parse_time_str(self, time_str):
        try:
            h, m = map(int, time_str.split(':'))
            return (0, m, True) if h == 24 else (h, m, False)
        except: return 0, 0, False

    def recalculate_schedule(self):
            times = self.event_data.get('times', [])
            if not times:
                self.status_label.setText(get_translation('status_unknown', 'UI'))
                self.target_time = None
                self.local_start_label.setText("")
                return
            now_utc = datetime.now(timezone.utc)
            candidates = []
            found_start_time_utc = None
            for slot in times:
                try:
                    sh, sm, s_next_day = self.parse_time_str(slot['start'])
                    eh, em, e_next_day = self.parse_time_str(slot['end'])
                    t_start = now_utc.replace(hour=sh, minute=sm, second=0, microsecond=0)
                    t_end = now_utc.replace(hour=eh, minute=em, second=0, microsecond=0)
                    if s_next_day: t_start += timedelta(days=1)
                    if e_next_day: t_end += timedelta(days=1)
                    if t_end <= t_start: t_end += timedelta(days=1)
                    if t_start <= now_utc < t_end:
                        self.is_active = True
                        self.target_time = t_end
                        found_start_time_utc = t_start
                        self.status_label.setText(get_translation('status_current', 'UI'))
                        self.setStatusStyle(active=True)
                        self.notify_cb.setVisible(False)
                        break
                    if t_start > now_utc: 
                        candidates.append(t_start)
                    else: 
                        candidates.append(t_start + timedelta(days=1))
                except: continue
            if not self.is_active:
                if candidates:
                    self.target_time = min(candidates)
                    found_start_time_utc = self.target_time
                    self.status_label.setText(get_translation('status_soon', 'UI'))
                    self.setStatusStyle(active=False)
                    self.notify_cb.setVisible(True)
                else:
                    self.target_time = None
                    found_start_time_utc = None
            if found_start_time_utc and SHOW_LOCAL_TIME and not self.is_active:
                try:
                    ts = found_start_time_utc.timestamp()
                    local_ts = time.localtime(ts)
                    self.local_start_label.setText(time.strftime("%H:%M", local_ts))
                except:
                    self.local_start_label.setText("")
            else:
                self.local_start_label.setText("")

    def setStatusStyle(self, active):
        current_style = self.styleSheet()
        if active:
            self.overlay.setStyleSheet("background-color: rgba(0, 0, 0, 0.85);") 
            if "border: 2px solid #ff5500" not in current_style:
                self.setStyleSheet(current_style + "QFrame#HudCard { border: 2px solid #ff5500; }")
        else:
            self.overlay.setStyleSheet("background-color: rgba(0, 0, 0, 0.70);")
            self.setup_background()

    def update_display(self):
        if not self.target_time or (datetime.now(timezone.utc) > self.target_time):
            self.recalculate_schedule()
            if not self.target_time: return False
        now = datetime.now(timezone.utc)
        diff = self.target_time - now
        total_seconds = int(diff.total_seconds())
        if total_seconds <= 0:
            self.recalculate_schedule()
            return False
        hours, remainder = divmod(total_seconds, 3600)
        minutes, seconds = divmod(remainder, 60)
        if hours > 0: self.timer_label.setText(f"{hours}h {minutes}m")
        else: self.timer_label.setText(f"{minutes:02}:{seconds:02}")
        self.flash_toggle = not self.flash_toggle 
        if self.is_active: new_color = "#ff5500" 
        elif total_seconds <= NOTIFY_SECONDS: new_color = "#ff5500" if self.flash_toggle else "#ffffff" 
        else: new_color = "#cccccc" 
        if self.last_style_state != new_color:
            self.timer_label.setStyleSheet(f"color: {new_color};")
            self.last_style_state = new_color
        if not self.is_active and self.notify_cb.isChecked() and not self.notified_state:
            if 0 < total_seconds <= NOTIFY_SECONDS: 
                self.notified_state = True 
                return True
        if total_seconds > (NOTIFY_SECONDS + 5): 
            self.notified_state = False
        return False

class MainWindow(QMainWindow):
    request_toggle_visibility = pyqtSignal()
    request_notification = pyqtSignal(str, str)

    def __init__(self):
        super().__init__()
        self.setWindowTitle(get_translation('app_title', 'UI'))
        self.setWindowFlags(Qt.WindowType.FramelessWindowHint | Qt.WindowType.WindowStaysOnTopHint | Qt.WindowType.Tool)
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        self.setStyleSheet(HUD_STYLESHEET)
        self.config = load_config()
        self.current_notify_min = self.config.getint('Settings', 'notify_minutes', fallback=DEFAULT_CONFIG['notify_minutes'])
        screen = QApplication.primaryScreen()
        geo = screen.availableGeometry()
        self.setGeometry(geo.x(), geo.y(), geo.width(), BANNER_HEIGHT)
        self.event_widgets = []
        self.game_detected_once = False
        self.audio_player = AudioPlayer(self)
        self.settings_button = None
        self.update_button = None 
        self.hotkey_thread = None 
        self.current_version = "1.1.0"
        self.latest_version = None
        self.latest_release_url = GITHUB_RELEASE_PAGE
        self.init_ui()
        self.setup_tray_icon()
        self.post_init_ui() 
        self.api_timer = QTimer()
        self.api_timer.timeout.connect(self.fetch_data)
        self.api_timer.start(REFRESH_API_INTERVAL_SEC * 1000)
        self.ui_timer = QTimer()
        self.ui_timer.timeout.connect(self.tick_ui)
        self.ui_timer.start(REFRESH_UI_INTERVAL_MS)
        self.hb_worker = HeartbeatWorker(self.current_version)
        self.hb_worker.start()
        QTimer.singleShot(500, self.fetch_data)
        QTimer.singleShot(1000, self.check_for_updates)
        self.request_toggle_visibility.connect(self.perform_toggle)
        self.request_notification.connect(self.show_in_game_notification)
        self.start_global_hotkey()
        if AUTO_CLOSE:
            self.game_monitor_timer = QTimer()
            self.game_monitor_timer.timeout.connect(self.monitor_game_process)
            self.game_monitor_timer.start(5000)

    def apply_configuration(self):
            print("Application de la configuration...")
            if self.ui_timer.isActive(): self.ui_timer.stop()
            if self.api_timer.isActive(): self.api_timer.stop()
            self.current_notify_min = NOTIFY_SECONDS // 60
            self.start_global_hotkey()
            self.setWindowTitle(get_translation('app_title', 'UI'))
            if hasattr(self, 'tray_icon') and self.tray_icon.contextMenu():
                actions = self.tray_icon.contextMenu().actions()
                if len(actions) >= 4:
                    actions[0].setText(get_translation('header', 'SETTINGS'))
                    actions[1].setText(get_translation('toggle_action', 'UI').format(hotkey=HOTKEY.upper()))
                    actions[3].setText(get_translation('quit_overlay_action', 'UI'))
            if self.update_button:
                self.update_button.setText(get_translation('update_available_button', 'UI'))
            temp_widgets = self.event_widgets.copy()
            self.event_widgets.clear()
            self.tabs.clear()
            for w in temp_widgets: w.deleteLater() 
            QTimer.singleShot(200, self.restart_loops)
            print(f"Config appliquée : Langue={CURRENT_LANGUAGE}, Hotkey={HOTKEY}")

    def restart_loops(self):
        self.fetch_data()
        self.api_timer.start(REFRESH_API_INTERVAL_SEC * 1000)
        self.ui_timer.start(REFRESH_UI_INTERVAL_MS)

    def check_for_updates(self):
        self.version_worker = GitHubVersionWorker()
        self.version_worker.version_checked.connect(self.handle_version_check)
        self.version_worker.start()

    def handle_version_check(self, latest_tag, release_url, exe_url):
            if not latest_tag: return
            self.latest_version = latest_tag
            self.latest_release_url = release_url
            try:
                current_parts = list(map(int, self.current_version.split('.')))
                latest_parts = list(map(int, self.latest_version.split('.')))
                is_newer = latest_parts > current_parts
            except ValueError:
                is_newer = False
            if is_newer:
                if exe_url:
                    print(f"Mise à jour dispo via : {exe_url}")
                    self.setup_url = exe_url 
                    try: self.update_button.clicked.disconnect()
                    except: pass 
                    self.update_button.clicked.connect(self.start_update_process)
                    self.update_button.setToolTip(get_translation('update_tooltip', 'UI'))
                else:
                    print("Mise à jour dispo, mais pas d'exe trouvé -> Lien Web")
                    try: self.update_button.clicked.disconnect()
                    except: pass
                    self.update_button.clicked.connect(self.open_release_page)
                self.show_update_button()

    def show_update_button(self):
            if self.update_button:
                self.update_button.setVisible(True)

    def open_release_page(self):
        QDesktopServices.openUrl(QUrl(self.latest_release_url))

    def sync_checkboxes(self, event_name, map_name, is_checked):
        for w in self.event_widgets:
            if w.event_data.get('name') == event_name and w.event_data.get('map') == map_name:
                w.notify_cb.blockSignals(True)
                w.notify_cb.setChecked(is_checked)
                w.notify_cb.blockSignals(False)

    def start_global_hotkey(self):
        if self.hotkey_thread:
            self.hotkey_thread.stop()
            self.hotkey_thread = None
        vk_code = get_vk_code(HOTKEY)
        self.hotkey_thread = NativeHotKey(key_code=vk_code)
        self.hotkey_thread.trigger.connect(self.perform_toggle)
        self.hotkey_thread.start()

    def force_quit(self):
        if self.hotkey_thread: self.hotkey_thread.stop()
        QApplication.quit()
        sys.exit(0)

    def open_settings(self):
            settings_window = SettingsWindow(self, HOTKEY, self.current_notify_min, self.current_version)
            settings_window.exec()

    def setup_tray_icon(self):
            self.tray_icon = QSystemTrayIcon(self)
            icon_path = resource_path(os.path.join("assets", "logo.png"))
            if os.path.exists(icon_path): self.tray_icon.setIcon(QIcon(icon_path))
            else: self.tray_icon.setIcon(QIcon())
            tray_menu = QMenu()
            settings_action = QAction(get_translation('header', 'SETTINGS'), self)
            settings_action.triggered.connect(self.open_settings)
            tray_menu.addAction(settings_action)
            toggle_action = QAction(get_translation('toggle_action', 'UI').format(hotkey=HOTKEY.upper()), self)
            toggle_action.triggered.connect(self.perform_toggle)
            tray_menu.addAction(toggle_action)
            tray_menu.addSeparator()
            quit_action = QAction(get_translation('quit_overlay_action', 'UI'), self)
            quit_action.triggered.connect(self.force_quit)
            tray_menu.addAction(quit_action)
            self.tray_icon.setContextMenu(tray_menu)
            self.tray_icon.show()

    def post_init_ui(self):
            self.msg_worker = MessageWorker()
            self.msg_worker.message_found.connect(self.display_banner_message)
            self.msg_worker.start()

    def display_banner_message(self, message):
            if message:
                prefix = get_translation('note_header', 'UI')
                if prefix == 'NOTE_HEADER': prefix = "NOTE :"
                self.msg_label.setText(f"{prefix} {message}")
                self.msg_label.setVisible(True)
            else:
                self.msg_label.setVisible(False)

    def monitor_game_process(self):
        hwnd = user32.FindWindowW(None, GAME_WINDOW_TITLE)
        game_is_running = hwnd != 0
        if game_is_running: self.game_detected_once = True 
        if self.game_detected_once and not game_is_running: self.force_quit()

    def perform_toggle(self):
        if self.isVisible(): self.hide()
        else:
            self.show()
            self.activateWindow()

    def init_ui(self):
            central = QWidget()
            central.setObjectName("CentralWidget")
            central.setStyleSheet("QWidget#CentralWidget { background-color: rgba(12, 12, 12, 210); border-bottom: 1px solid #444; }")
            main_layout = QVBoxLayout(central)
            main_layout.setContentsMargins(0, 0, 0, 0)
            self.header_bar = QFrame(central)
            self.header_bar.setFixedHeight(40) 
            self.header_bar.setStyleSheet("background-color: transparent;") 
            header_layout = QHBoxLayout(self.header_bar)
            header_layout.setContentsMargins(10, 5, 10, 5)
            header_layout.setSpacing(10)
            self.msg_label = QLabel()
            self.msg_label.setVisible(False)
            self.msg_label.setStyleSheet("""
                background-color: #000000; color: #ff5500; font-weight: bold; font-size: 11px; text-transform: uppercase;
                padding: 4px 8px; border-radius: 4px; border: 1px solid #333;
            """)
            header_layout.addWidget(self.msg_label)
            header_layout.addStretch()
            self.update_button = QPushButton(get_translation('update_available_button', 'UI'))
            self.update_button.setVisible(False)
            self.update_button.setCursor(Qt.CursorShape.PointingHandCursor)
            self.update_button.setStyleSheet("""
                QPushButton { background-color: #008000; color: white; font-weight: bold; padding: 4px 10px; border: none; border-radius: 3px; text-transform: uppercase; font-size: 10px; }
                QPushButton:hover { background-color: #00a000; }
            """)
            self.update_button.clicked.connect(self.open_release_page)
            header_layout.addWidget(self.update_button)
            icon_settings_path = resource_path(os.path.join("assets", "settings.png"))
            self.settings_button = QPushButton()
            if os.path.exists(icon_settings_path): self.settings_button.setIcon(QIcon(icon_settings_path))
            else: self.settings_button.setText("O")
            self.settings_button.setIconSize(QSize(20, 20))
            self.settings_button.setFixedSize(28, 28)
            self.settings_button.setCursor(Qt.CursorShape.PointingHandCursor)
            self.settings_button.setStyleSheet("QPushButton { background-color: transparent; border: none; }")
            self.settings_button.clicked.connect(self.open_settings)
            header_layout.addWidget(self.settings_button)
            self.tabs = QTabWidget()
            self.tabs.setDocumentMode(True)
            main_layout.addWidget(self.tabs)
            self.setCentralWidget(central)
        
    def resizeEvent(self, event):
            if hasattr(self, 'header_bar'):
                self.header_bar.resize(self.centralWidget().width(), 40)
                self.header_bar.raise_() 
            super().resizeEvent(event)

    def reposition_header_note(self):
            if hasattr(self, 'msg_label') and self.msg_label.isVisible():
                cw = self.centralWidget().width()
                lbl_w = self.msg_label.width()
                occupied_space_right = 50 
                if self.update_button and self.update_button.isVisible():
                    occupied_space_right += self.update_button.width() + 10
                target_x = cw - occupied_space_right - lbl_w
                if target_x < 10: target_x = 10
                self.msg_label.move(int(target_x), 10)
                self.msg_label.raise_()

    def fetch_data(self):
            self.worker = ApiWorker()
            self.worker.data_fetched.connect(self.load_data)
            self.worker.error_occurred.connect(lambda err: print(f"Signal d'erreur reçu : {err}"))
            self.worker.start()
            self.msg_worker = MessageWorker()
            self.msg_worker.message_found.connect(self.display_banner_message)
            self.msg_worker.start()

    def load_data(self, data):
            self.setUpdatesEnabled(False) 
            try:
                saved_states = {}
                for w in self.event_widgets:
                    try:
                        name = w.event_data.get('name')
                        map_name = w.event_data.get('map')
                        unique_key = f"{name}_{map_name}"
                        is_checked = w.notify_cb.isChecked()
                        has_notified = w.notified_state
                        if unique_key in saved_states:
                            if has_notified: saved_states[unique_key]['notified'] = True
                            if is_checked: saved_states[unique_key]['checked'] = True
                        else:
                            saved_states[unique_key] = {'checked': is_checked, 'notified': has_notified}
                    except: pass
                current_idx = self.tabs.currentIndex()
                current_name = self.tabs.tabText(current_idx) if current_idx >= 0 else ""
                self.tabs.clear()
                self.event_widgets.clear()
                if not data: return
                grouped = {}
                all_events = [] 
                for item in data:
                    all_events.append(item)
                    cat = item.get('name', 'Autre')
                    if cat not in grouped: grouped[cat] = []
                    grouped[cat].append(item)
                self.create_tab(get_translation('ALL', 'TABS'), all_events, saved_states)
                cats_sorted = []
                for cat_en, events in grouped.items():
                    cats_sorted.append((get_translation(cat_en, 'TABS'), events))
                cats_sorted.sort(key=lambda x: x[0])
                for cat_fr, events in cats_sorted:
                    self.create_tab(cat_fr, events, saved_states)
                for i in range(self.tabs.count()):
                    if self.tabs.tabText(i) == current_name:
                        self.tabs.setCurrentIndex(i)
                        break
            finally:
                self.setUpdatesEnabled(True)

    def create_tab(self, tab_name, events_list, saved_states=None):
            if saved_states is None: saved_states = {}
            tab_page = QWidget()
            page_layout = QVBoxLayout(tab_page)
            page_layout.setContentsMargins(0, 0, 0, 0)
            scroll = QScrollArea()
            scroll.setWidgetResizable(True)
            scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
            scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
            scroll_content = QWidget()
            scroll_content.setObjectName("ScrollContent")
            cards_layout = QHBoxLayout(scroll_content)
            cards_layout.setContentsMargins(10, 10, 10, 10)
            cards_layout.setSpacing(10)
            cards_layout.setAlignment(Qt.AlignmentFlag.AlignLeft)
            temp_widgets = []
            for evt in events_list:
                w = HudEventCard(evt, parent=scroll_content) 
                name = evt.get('name')
                map_name = evt.get('map')
                unique_key = f"{name}_{map_name}"
                w.state_changed.connect(self.sync_checkboxes)
                if unique_key in saved_states:
                    state = saved_states[unique_key]
                    if state.get('checked', False): w.notify_cb.setChecked(True)
                    if state.get('notified', False): w.notified_state = True
                temp_widgets.append(w)
            temp_widgets.sort(key=lambda w: (
                not w.is_active, 
                w.target_time or datetime.max.replace(tzinfo=timezone.utc)
            ))
            for w in temp_widgets:
                self.event_widgets.append(w)
                cards_layout.addWidget(w)
            cards_layout.addStretch()
            scroll.setWidget(scroll_content)
            page_layout.addWidget(scroll)
            self.tabs.addTab(tab_page, tab_name)

    def tick_ui(self):
                if not self.event_widgets: return
                for w in list(self.event_widgets):
                    try:
                        if not w: continue
                        if w.update_display(): self.trigger_notification(w.event_data)
                    except RuntimeError: pass
                    except Exception as e: print(f"Erreur UI Tick: {e}")

    def trigger_notification(self, data):
        cat = get_translation(data['name'], 'TABS')
        map_n = get_translation(data.get('map'), 'MAPS')
        minutes = NOTIFY_SECONDS // 60
        title = f"ARC: {cat}"
        msg = get_translation('notify_message', 'UI').format(minutes=minutes, map_name=map_n)
        self.request_notification.emit(title, msg)
        if SOUND_ENABLED: self.audio_player.play_sound(SOUND_FILE)

    def show_in_game_notification(self, title, message):
        self.toast = ToastNotification(title, message)
        self.toast.show_toast()

    def restart_application(self):
        if getattr(sys, 'frozen', False): args = [sys.executable]
        else: args = [sys.executable, __file__]
        subprocess.Popen(args)
        self.force_quit()

    def start_update_process(self):
        if self.update_button:
            self.update_button.setEnabled(False)
            self.update_button.setText(get_translation('update_downloading', 'UI'))
        
        if hasattr(self, 'setup_url'):
            self.downloader = UpdateDownloader(self.setup_url)
            self.downloader.progress.connect(self.update_download_progress)
            self.downloader.finished.connect(self.run_installer)
            self.downloader.start()
        else:
            self.open_release_page()

    def update_download_progress(self, percent):
        if self.update_button:
            self.update_button.setText(f"{percent}%")

    def run_installer(self, file_path):
        if self.update_button:
            self.update_button.setText(get_translation('update_installing', 'UI'))
        try:
            subprocess.Popen([file_path, "/SILENT", "/SP-", "/CLOSEAPPLICATIONS"])
            self.force_quit()
        except Exception as e:
            print(f"Erreur lancement installateur: {e}")
            if self.update_button:
                self.update_button.setText(get_translation('update_error', 'UI'))
                self.update_button.setEnabled(True)
                try: self.update_button.clicked.disconnect()
                except: pass
                self.update_button.clicked.connect(self.open_release_page)

if __name__ == "__main__":
    load_config() 
    app = QApplication(sys.argv)
    icon_global_path = resource_path(os.path.join("assets", "logo.png"))
    if os.path.exists(icon_global_path): app.setWindowIcon(QIcon(icon_global_path))
    if hasattr(Qt.ApplicationAttribute, 'AA_UseHighDpiPixmaps'):
        app.setAttribute(Qt.ApplicationAttribute.AA_UseHighDpiPixmaps)
    window = MainWindow()
    window.show()
    sys.exit(app.exec())
