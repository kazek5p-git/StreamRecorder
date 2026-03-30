#![cfg_attr(target_os = "windows", windows_subsystem = "windows")]

extern crate native_windows_gui as nwg;

use nwg::NativeUi;
use once_cell::sync::OnceCell;
use std::cell::RefCell;
use std::path::{Path, PathBuf};
use std::process::Command;
use std::rc::Rc;
use std::sync::Arc;
use std::time::Duration;
use streamrecorder::app_context::AppContext;
use streamrecorder::config::{AppPaths, load_or_create};
use streamrecorder::localization::{current_language, tr};
use streamrecorder::models::{AppSettings, Credentials, ScheduleRule, Station};
use streamrecorder::recording::RecordingSnapshot;
use streamrecorder::updater::{download_update, install_downloaded_update};
use uuid::Uuid;
use windows::Win32::Foundation::HWND;
use windows::Win32::System::Power::{
    ES_CONTINUOUS, ES_DISPLAY_REQUIRED, ES_SYSTEM_REQUIRED, SetThreadExecutionState,
};
use windows::Win32::UI::WindowsAndMessaging::{
    BringWindowToTop, GWL_STYLE, GetWindowLongW, HWND_NOTOPMOST, HWND_TOPMOST, SWP_NOMOVE,
    SWP_NOSIZE, SetForegroundWindow, SetWindowLongW, SetWindowPos, WM_CONTEXTMENU, WS_TABSTOP,
};

const GUARDED_ARG: &str = "--guarded";

static APP_CONTEXT: OnceCell<Arc<AppContext>> = OnceCell::new();

fn app_context() -> &'static Arc<AppContext> {
    APP_CONTEXT.get().expect("app context not initialized")
}

struct MainWindow {
    current_station_id: RefCell<Option<Uuid>>,
    station_editor_station_id: RefCell<Option<Uuid>>,
    station_rows: RefCell<Vec<Uuid>>,
    last_log_text: RefCell<String>,
    log_visible: RefCell<bool>,
    window: nwg::Window,
    file_menu: nwg::Menu,
    file_add: nwg::MenuItem,
    file_edit: nwg::MenuItem,
    file_start: nwg::MenuItem,
    file_stop: nwg::MenuItem,
    file_delete: nwg::MenuItem,
    system_open_recordings: nwg::MenuItem,
    system_open_settings: nwg::MenuItem,
    file_settings: nwg::MenuItem,
    file_exit: nwg::MenuItem,
    help_menu: nwg::Menu,
    help_updates: nwg::MenuItem,
    help_about: nwg::MenuItem,
    new_button: nwg::Button,
    show_log_button: nwg::Button,
    station_list: nwg::ListView,
    status_bar: nwg::Label,
    log_window: nwg::Window,
    log_box: nwg::ListBox<String>,
    log_close: nwg::Button,
    timer: nwg::AnimationTimer,
    icon: nwg::Icon,
    tray_window: nwg::MessageWindow,
    tray: nwg::TrayNotification,
    tray_menu: nwg::Menu,
    tray_show: nwg::MenuItem,
    tray_settings: nwg::MenuItem,
    tray_exit: nwg::MenuItem,
    popup_menu: nwg::Menu,
    popup_add: nwg::MenuItem,
    popup_edit: nwg::MenuItem,
    popup_start: nwg::MenuItem,
    popup_stop: nwg::MenuItem,
    popup_delete: nwg::MenuItem,
    station_window: nwg::Window,
    station_tabs: nwg::TabsContainer,
    station_info_tab: nwg::Tab,
    station_schedule_tab: nwg::Tab,
    station_name_label: nwg::Label,
    name_input: nwg::TextInput,
    url_label: nwg::Label,
    url_input: nwg::TextInput,
    user_label: nwg::Label,
    user_input: nwg::TextInput,
    pass_label: nwg::Label,
    pass_input: nwg::TextInput,
    schedule_enabled: nwg::CheckBox,
    day_mon: nwg::CheckBox,
    day_tue: nwg::CheckBox,
    day_wed: nwg::CheckBox,
    day_thu: nwg::CheckBox,
    day_fri: nwg::CheckBox,
    day_sat: nwg::CheckBox,
    day_sun: nwg::CheckBox,
    start_label: nwg::Label,
    start_input: nwg::TextInput,
    end_label: nwg::Label,
    end_input: nwg::TextInput,
    station_ok: nwg::Button,
    station_cancel: nwg::Button,
    settings_window: nwg::Window,
    settings_general_label: nwg::Label,
    settings_folder_label: nwg::Label,
    settings_template_label: nwg::Label,
    settings_template_help: nwg::Label,
    settings_other_label: nwg::Label,
    settings_launch_on_startup: nwg::CheckBox,
    settings_always_on_top: nwg::CheckBox,
    settings_minimize_to_tray: nwg::CheckBox,
    settings_confirm_on_exit: nwg::CheckBox,
    settings_restart_on_crash: nwg::CheckBox,
    settings_prevent_sleep: nwg::CheckBox,
    settings_start_minimized: nwg::CheckBox,
    settings_recordings_folder_input: nwg::TextInput,
    settings_recordings_folder_browse: nwg::Button,
    settings_template_input: nwg::TextInput,
    settings_remux_raw_aac: nwg::CheckBox,
    settings_language_label: nwg::Label,
    settings_language_combo: nwg::ComboBox<String>,
    settings_update_repo_label: nwg::Label,
    settings_update_repo_input: nwg::TextInput,
    settings_save: nwg::Button,
    settings_cancel: nwg::Button,
    settings_folder_dialog: nwg::FileDialog,
    handler: RefCell<Option<nwg::EventHandler>>,
    station_handler: RefCell<Option<nwg::EventHandler>>,
    settings_handler: RefCell<Option<nwg::EventHandler>>,
    log_handler: RefCell<Option<nwg::EventHandler>>,
    tray_handler: RefCell<Option<nwg::EventHandler>>,
    raw_handlers: RefCell<Vec<nwg::RawEventHandler>>,
}

impl Default for MainWindow {
    fn default() -> Self {
        Self {
            current_station_id: RefCell::new(None),
            station_editor_station_id: RefCell::new(None),
            station_rows: RefCell::new(Vec::new()),
            last_log_text: RefCell::new(String::new()),
            log_visible: RefCell::new(false),
            window: Default::default(),
            file_menu: Default::default(),
            file_add: Default::default(),
            file_edit: Default::default(),
            file_start: Default::default(),
            file_stop: Default::default(),
            file_delete: Default::default(),
            system_open_recordings: Default::default(),
            system_open_settings: Default::default(),
            file_settings: Default::default(),
            file_exit: Default::default(),
            help_menu: Default::default(),
            help_updates: Default::default(),
            help_about: Default::default(),
            new_button: Default::default(),
            show_log_button: Default::default(),
            station_list: Default::default(),
            status_bar: Default::default(),
            log_window: Default::default(),
            log_box: Default::default(),
            log_close: Default::default(),
            timer: Default::default(),
            icon: Default::default(),
            tray_window: Default::default(),
            tray: Default::default(),
            tray_menu: Default::default(),
            tray_show: Default::default(),
            tray_settings: Default::default(),
            tray_exit: Default::default(),
            popup_menu: Default::default(),
            popup_add: Default::default(),
            popup_edit: Default::default(),
            popup_start: Default::default(),
            popup_stop: Default::default(),
            popup_delete: Default::default(),
            station_window: Default::default(),
            station_tabs: Default::default(),
            station_info_tab: Default::default(),
            station_schedule_tab: Default::default(),
            station_name_label: Default::default(),
            name_input: Default::default(),
            url_label: Default::default(),
            url_input: Default::default(),
            user_label: Default::default(),
            user_input: Default::default(),
            pass_label: Default::default(),
            pass_input: Default::default(),
            schedule_enabled: Default::default(),
            day_mon: Default::default(),
            day_tue: Default::default(),
            day_wed: Default::default(),
            day_thu: Default::default(),
            day_fri: Default::default(),
            day_sat: Default::default(),
            day_sun: Default::default(),
            start_label: Default::default(),
            start_input: Default::default(),
            end_label: Default::default(),
            end_input: Default::default(),
            station_ok: Default::default(),
            station_cancel: Default::default(),
            settings_window: Default::default(),
            settings_general_label: Default::default(),
            settings_folder_label: Default::default(),
            settings_template_label: Default::default(),
            settings_template_help: Default::default(),
            settings_other_label: Default::default(),
            settings_launch_on_startup: Default::default(),
            settings_always_on_top: Default::default(),
            settings_minimize_to_tray: Default::default(),
            settings_confirm_on_exit: Default::default(),
            settings_restart_on_crash: Default::default(),
            settings_prevent_sleep: Default::default(),
            settings_start_minimized: Default::default(),
            settings_recordings_folder_input: Default::default(),
            settings_recordings_folder_browse: Default::default(),
            settings_template_input: Default::default(),
            settings_remux_raw_aac: Default::default(),
            settings_language_label: Default::default(),
            settings_language_combo: Default::default(),
            settings_update_repo_label: Default::default(),
            settings_update_repo_input: Default::default(),
            settings_save: Default::default(),
            settings_cancel: Default::default(),
            settings_folder_dialog: Default::default(),
            handler: RefCell::new(None),
            station_handler: RefCell::new(None),
            settings_handler: RefCell::new(None),
            log_handler: RefCell::new(None),
            tray_handler: RefCell::new(None),
            raw_handlers: RefCell::new(Vec::new()),
        }
    }
}

struct MainWindowUi {
    inner: Rc<MainWindow>,
}

impl nwg::NativeUi<MainWindowUi> for MainWindow {
    fn build_ui(mut data: MainWindow) -> Result<MainWindowUi, nwg::NwgError> {
        nwg::Icon::builder()
            .source_system(Some(nwg::OemIcon::Information))
            .build(&mut data.icon)?;

        nwg::Window::builder()
            .flags(
                nwg::WindowFlags::WINDOW
                    | nwg::WindowFlags::MINIMIZE_BOX
                    | nwg::WindowFlags::VISIBLE,
            )
            .size((980, 580))
            .position((100, 60))
            .title("StreamRecorder")
            .icon(Some(&data.icon))
            .build(&mut data.window)?;

        nwg::Menu::builder()
            .text(tr("&File"))
            .parent(&data.window)
            .build(&mut data.file_menu)?;
        nwg::MenuItem::builder()
            .text(tr("Add station"))
            .parent(&data.file_menu)
            .build(&mut data.file_add)?;
        nwg::MenuItem::builder()
            .text(tr("Edit station"))
            .parent(&data.file_menu)
            .build(&mut data.file_edit)?;
        nwg::MenuItem::builder()
            .text(tr("Start recording"))
            .parent(&data.file_menu)
            .build(&mut data.file_start)?;
        nwg::MenuItem::builder()
            .text(tr("Stop recording"))
            .parent(&data.file_menu)
            .build(&mut data.file_stop)?;
        nwg::MenuItem::builder()
            .text(tr("Delete station"))
            .parent(&data.file_menu)
            .build(&mut data.file_delete)?;
        nwg::MenuItem::builder()
            .text(tr("Open recordings folder"))
            .parent(&data.file_menu)
            .build(&mut data.system_open_recordings)?;
        nwg::MenuItem::builder()
            .text(tr("Open settings folder"))
            .parent(&data.file_menu)
            .build(&mut data.system_open_settings)?;
        nwg::MenuItem::builder()
            .text(tr("Settings"))
            .parent(&data.file_menu)
            .build(&mut data.file_settings)?;
        nwg::MenuItem::builder()
            .text(tr("Exit"))
            .parent(&data.file_menu)
            .build(&mut data.file_exit)?;
        nwg::Menu::builder()
            .text(tr("&Help"))
            .parent(&data.window)
            .build(&mut data.help_menu)?;
        nwg::MenuItem::builder()
            .text(tr("Check for updates"))
            .parent(&data.help_menu)
            .build(&mut data.help_updates)?;
        nwg::MenuItem::builder()
            .text(tr("About"))
            .parent(&data.help_menu)
            .build(&mut data.help_about)?;

        build_button(
            &data.window,
            &mut data.new_button,
            tr("Add station"),
            (10, 10),
            (120, 28),
        )?;
        build_button(
            &data.window,
            &mut data.show_log_button,
            tr("Show log"),
            (840, 10),
            (130, 28),
        )?;

        nwg::ListView::builder()
            .parent(&data.window)
            .position((10, 45))
            .size((960, 455))
            .focus(true)
            .list_style(nwg::ListViewStyle::Detailed)
            .flags(
                nwg::ListViewFlags::VISIBLE
                    | nwg::ListViewFlags::TAB_STOP
                    | nwg::ListViewFlags::SINGLE_SELECTION
                    | nwg::ListViewFlags::ALWAYS_SHOW_SELECTION,
            )
            .ex_flags(nwg::ListViewExFlags::FULL_ROW_SELECT | nwg::ListViewExFlags::GRID)
            .build(&mut data.station_list)?;

        build_label(
            &data.window,
            &mut data.status_bar,
            &format!("{} 0", tr("Currently recording:")),
            (10, 510),
            (320, 22),
        )?;

        nwg::Window::builder()
            .flags(nwg::WindowFlags::WINDOW)
            .size((760, 430))
            .position((140, 100))
            .title(tr("Log"))
            .icon(Some(&data.icon))
            .parent(Some(&data.window))
            .build(&mut data.log_window)?;
        build_button(
            &data.log_window,
            &mut data.log_close,
            tr("Close"),
            (660, 365),
            (90, 28),
        )?;
        nwg::ListBox::builder()
            .parent(&data.log_window)
            .position((10, 10))
            .size((740, 340))
            .flags(nwg::ListBoxFlags::VISIBLE | nwg::ListBoxFlags::TAB_STOP)
            .build(&mut data.log_box)?;

        nwg::AnimationTimer::builder()
            .parent(&data.window)
            .interval(Duration::from_secs(1))
            .active(true)
            .build(&mut data.timer)?;
        nwg::MessageWindow::builder().build(&mut data.tray_window)?;
        nwg::TrayNotification::builder()
            .parent(&data.tray_window)
            .icon(Some(&data.icon))
            .tip(Some("StreamRecorder"))
            .build(&mut data.tray)?;
        nwg::Menu::builder()
            .popup(true)
            .parent(&data.tray_window)
            .build(&mut data.tray_menu)?;
        nwg::MenuItem::builder()
            .text(tr("Show"))
            .parent(&data.tray_menu)
            .build(&mut data.tray_show)?;
        nwg::MenuItem::builder()
            .text(tr("Settings"))
            .parent(&data.tray_menu)
            .build(&mut data.tray_settings)?;
        nwg::MenuItem::builder()
            .text(tr("Exit"))
            .parent(&data.tray_menu)
            .build(&mut data.tray_exit)?;
        nwg::Menu::builder()
            .popup(true)
            .parent(&data.window)
            .build(&mut data.popup_menu)?;
        nwg::MenuItem::builder()
            .text(tr("Add station"))
            .parent(&data.popup_menu)
            .build(&mut data.popup_add)?;
        nwg::MenuItem::builder()
            .text(tr("Edit station"))
            .parent(&data.popup_menu)
            .build(&mut data.popup_edit)?;
        nwg::MenuItem::builder()
            .text(tr("Start recording"))
            .parent(&data.popup_menu)
            .build(&mut data.popup_start)?;
        nwg::MenuItem::builder()
            .text(tr("Stop recording"))
            .parent(&data.popup_menu)
            .build(&mut data.popup_stop)?;
        nwg::MenuItem::builder()
            .text(tr("Delete station"))
            .parent(&data.popup_menu)
            .build(&mut data.popup_delete)?;

        data.station_list.insert_column(tr("Station"));
        data.station_list.insert_column(nwg::InsertListViewColumn {
            index: Some(1),
            width: Some(330),
            text: Some("URL".to_string()),
            ..Default::default()
        });
        data.station_list.insert_column(nwg::InsertListViewColumn {
            index: Some(2),
            width: Some(170),
            text: Some(tr("Status").to_string()),
            ..Default::default()
        });
        data.station_list.insert_column(nwg::InsertListViewColumn {
            index: Some(3),
            width: Some(80),
            text: Some(tr("Format").to_string()),
            ..Default::default()
        });
        data.station_list.insert_column(nwg::InsertListViewColumn {
            index: Some(4),
            width: Some(190),
            text: Some(tr("File").to_string()),
            ..Default::default()
        });
        data.station_list.set_headers_enabled(true);

        nwg::Window::builder()
            .flags(nwg::WindowFlags::WINDOW)
            .size((540, 420))
            .position((170, 100))
            .title(tr("Add station"))
            .icon(Some(&data.icon))
            .parent(Some(&data.window))
            .build(&mut data.station_window)?;
        nwg::TabsContainer::builder()
            .parent(&data.station_window)
            .position((10, 10))
            .size((510, 325))
            .build(&mut data.station_tabs)?;
        nwg::Tab::builder()
            .text(tr("Information"))
            .parent(&data.station_tabs)
            .build(&mut data.station_info_tab)?;
        nwg::Tab::builder()
            .text(tr("Schedule"))
            .parent(&data.station_tabs)
            .build(&mut data.station_schedule_tab)?;
        build_label(
            &data.station_info_tab,
            &mut data.station_name_label,
            tr("Name:"),
            (20, 20),
            (90, 22),
        )?;
        build_input(
            &data.station_info_tab,
            &mut data.name_input,
            "",
            (120, 17),
            (350, 26),
            false,
        )?;
        build_label(
            &data.station_info_tab,
            &mut data.url_label,
            "URL:",
            (20, 55),
            (90, 22),
        )?;
        build_input(
            &data.station_info_tab,
            &mut data.url_input,
            "",
            (120, 52),
            (350, 26),
            false,
        )?;
        build_label(
            &data.station_info_tab,
            &mut data.user_label,
            tr("Username:"),
            (20, 90),
            (90, 22),
        )?;
        build_input(
            &data.station_info_tab,
            &mut data.user_input,
            "",
            (120, 87),
            (350, 26),
            false,
        )?;
        build_label(
            &data.station_info_tab,
            &mut data.pass_label,
            tr("Password:"),
            (20, 125),
            (90, 22),
        )?;
        build_input(
            &data.station_info_tab,
            &mut data.pass_input,
            "",
            (120, 122),
            (350, 26),
            true,
        )?;
        nwg::CheckBox::builder()
            .text(tr("Enable schedule"))
            .parent(&data.station_schedule_tab)
            .position((20, 20))
            .size((180, 24))
            .build(&mut data.schedule_enabled)?;
        build_day(
            &data.station_schedule_tab,
            &mut data.day_mon,
            tr("Mon"),
            (20, 60),
        )?;
        build_day(
            &data.station_schedule_tab,
            &mut data.day_tue,
            tr("Tue"),
            (80, 60),
        )?;
        build_day(
            &data.station_schedule_tab,
            &mut data.day_wed,
            tr("Wed"),
            (140, 60),
        )?;
        build_day(
            &data.station_schedule_tab,
            &mut data.day_thu,
            tr("Thu"),
            (200, 60),
        )?;
        build_day(
            &data.station_schedule_tab,
            &mut data.day_fri,
            tr("Fri"),
            (260, 60),
        )?;
        build_day(
            &data.station_schedule_tab,
            &mut data.day_sat,
            tr("Sat"),
            (20, 90),
        )?;
        build_day(
            &data.station_schedule_tab,
            &mut data.day_sun,
            tr("Sun"),
            (80, 90),
        )?;
        build_label(
            &data.station_schedule_tab,
            &mut data.start_label,
            tr("Start HH:MM:"),
            (20, 135),
            (90, 22),
        )?;
        build_input(
            &data.station_schedule_tab,
            &mut data.start_input,
            "00:00",
            (120, 132),
            (90, 26),
            false,
        )?;
        build_label(
            &data.station_schedule_tab,
            &mut data.end_label,
            tr("End HH:MM:"),
            (230, 135),
            (90, 22),
        )?;
        build_input(
            &data.station_schedule_tab,
            &mut data.end_input,
            "23:59",
            (320, 132),
            (90, 26),
            false,
        )?;
        build_button(
            &data.station_window,
            &mut data.station_ok,
            tr("OK"),
            (330, 350),
            (90, 28),
        )?;
        build_button(
            &data.station_window,
            &mut data.station_cancel,
            tr("Cancel"),
            (430, 350),
            (90, 28),
        )?;

        nwg::Window::builder()
            .flags(nwg::WindowFlags::WINDOW)
            .size((660, 520))
            .position((160, 110))
            .title(tr("Settings"))
            .icon(Some(&data.icon))
            .parent(Some(&data.window))
            .build(&mut data.settings_window)?;

        build_label(
            &data.settings_window,
            &mut data.settings_general_label,
            tr("General"),
            (10, 10),
            (140, 20),
        )?;
        nwg::CheckBox::builder()
            .text(tr("Launch application at Windows startup"))
            .parent(&data.settings_window)
            .position((20, 35))
            .size((360, 22))
            .build(&mut data.settings_launch_on_startup)?;
        nwg::CheckBox::builder()
            .text(tr("Always on top"))
            .parent(&data.settings_window)
            .position((20, 60))
            .size((260, 22))
            .build(&mut data.settings_always_on_top)?;
        nwg::CheckBox::builder()
            .text(tr("Minimize to system tray"))
            .parent(&data.settings_window)
            .position((20, 85))
            .size((320, 22))
            .build(&mut data.settings_minimize_to_tray)?;
        nwg::CheckBox::builder()
            .text(tr("Ask for confirmation before exit"))
            .parent(&data.settings_window)
            .position((20, 110))
            .size((300, 22))
            .build(&mut data.settings_confirm_on_exit)?;
        nwg::CheckBox::builder()
            .text(tr("Restart program after a crash"))
            .parent(&data.settings_window)
            .position((20, 135))
            .size((280, 22))
            .build(&mut data.settings_restart_on_crash)?;
        nwg::CheckBox::builder()
            .text(tr("Prevent the computer from sleeping"))
            .parent(&data.settings_window)
            .position((20, 160))
            .size((380, 22))
            .build(&mut data.settings_prevent_sleep)?;
        nwg::CheckBox::builder()
            .text(tr("Start minimized"))
            .parent(&data.settings_window)
            .position((20, 185))
            .size((260, 22))
            .build(&mut data.settings_start_minimized)?;
        build_label(
            &data.settings_window,
            &mut data.settings_folder_label,
            tr("Recordings folder:"),
            (20, 230),
            (120, 22),
        )?;
        build_input(
            &data.settings_window,
            &mut data.settings_recordings_folder_input,
            "",
            (150, 227),
            (370, 26),
            false,
        )?;
        build_button(
            &data.settings_window,
            &mut data.settings_recordings_folder_browse,
            tr("Browse"),
            (530, 225),
            (90, 28),
        )?;
        build_label(
            &data.settings_window,
            &mut data.settings_template_label,
            tr("File name template:"),
            (20, 265),
            (130, 22),
        )?;
        build_input(
            &data.settings_window,
            &mut data.settings_template_input,
            "",
            (150, 262),
            (470, 26),
            false,
        )?;
        build_label(
            &data.settings_window,
            &mut data.settings_template_help,
            tr("Tokens: %t station, %r year, %M month, %d day, %h hour, %m minute, %s second"),
            (20, 295),
            (600, 36),
        )?;
        build_label(
            &data.settings_window,
            &mut data.settings_other_label,
            tr("Other"),
            (10, 340),
            (140, 20),
        )?;
        nwg::CheckBox::builder()
            .text(tr("Remux RAW AAC to M4A after recording"))
            .parent(&data.settings_window)
            .position((20, 365))
            .size((360, 22))
            .build(&mut data.settings_remux_raw_aac)?;
        build_label(
            &data.settings_window,
            &mut data.settings_language_label,
            tr("Language:"),
            (20, 395),
            (140, 22),
        )?;
        nwg::ComboBox::builder()
            .parent(&data.settings_window)
            .position((170, 392))
            .size((150, 120))
            .collection(language_options())
            .selected_index(Some(language_selection_index()))
            .build(&mut data.settings_language_combo)?;
        build_label(
            &data.settings_window,
            &mut data.settings_update_repo_label,
            tr("Update repository:"),
            (20, 427),
            (140, 22),
        )?;
        build_input(
            &data.settings_window,
            &mut data.settings_update_repo_input,
            "",
            (170, 424),
            (470, 26),
            false,
        )?;
        build_button(
            &data.settings_window,
            &mut data.settings_save,
            tr("Save"),
            (450, 455),
            (90, 28),
        )?;
        build_button(
            &data.settings_window,
            &mut data.settings_cancel,
            tr("Cancel"),
            (550, 455),
            (90, 28),
        )?;
        nwg::FileDialog::builder()
            .title(tr("Choose recordings folder"))
            .action(nwg::FileDialogAction::OpenDirectory)
            .build(&mut data.settings_folder_dialog)?;

        let ui = MainWindowUi {
            inner: Rc::new(data),
        };
        bind_events(&ui);
        bind_station_events(&ui);
        bind_settings_events(&ui);
        bind_log_events(&ui);
        bind_tray_events(&ui);
        bind_raw_handlers(&ui);
        add_tab_stop_style(&ui.inner.station_tabs.handle);
        ui.inner.setup();
        Ok(ui)
    }
}

fn build_button<C: Into<nwg::ControlHandle>>(
    parent: C,
    button: &mut nwg::Button,
    text: &str,
    pos: (i32, i32),
    size: (i32, i32),
) -> Result<(), nwg::NwgError> {
    nwg::Button::builder()
        .text(text)
        .parent(parent)
        .position(pos)
        .size(size)
        .build(button)
}

fn build_label<C: Into<nwg::ControlHandle>>(
    parent: C,
    label: &mut nwg::Label,
    text: &str,
    pos: (i32, i32),
    size: (i32, i32),
) -> Result<(), nwg::NwgError> {
    nwg::Label::builder()
        .text(text)
        .parent(parent)
        .position(pos)
        .size(size)
        .build(label)
}

fn build_input<C: Into<nwg::ControlHandle>>(
    parent: C,
    input: &mut nwg::TextInput,
    text: &str,
    pos: (i32, i32),
    size: (i32, i32),
    password: bool,
) -> Result<(), nwg::NwgError> {
    let mut builder = nwg::TextInput::builder();
    builder = builder.text(text).parent(parent).position(pos).size(size);
    if password {
        builder = builder.password(Some('*'));
    }
    builder.build(input)
}

fn build_day<C: Into<nwg::ControlHandle>>(
    parent: C,
    checkbox: &mut nwg::CheckBox,
    text: &str,
    pos: (i32, i32),
) -> Result<(), nwg::NwgError> {
    nwg::CheckBox::builder()
        .text(text)
        .parent(parent)
        .position(pos)
        .size((55, 24))
        .build(checkbox)?;
    checkbox.set_check_state(nwg::CheckBoxState::Checked);
    Ok(())
}

fn bind_events(ui: &MainWindowUi) {
    use nwg::Event as E;
    let weak = Rc::downgrade(&ui.inner);
    let handler =
        nwg::full_bind_event_handler(&ui.inner.window.handle, move |evt, evt_data, handle| {
            let Some(app) = weak.upgrade() else {
                return;
            };
            match evt {
                E::OnWindowClose if handle == app.window => app.on_window_close(&evt_data),
                E::OnWindowMinimize if handle == app.window => app.on_window_minimize(),
                E::OnButtonClick if handle == app.new_button => app.open_new_station_dialog(),
                E::OnButtonClick if handle == app.show_log_button => app.toggle_log(),
                E::OnMenuItemSelected if handle == app.file_add => app.open_new_station_dialog(),
                E::OnMenuItemSelected if handle == app.file_edit => {
                    app.open_selected_station_dialog()
                }
                E::OnMenuItemSelected if handle == app.file_start => app.start_selected(),
                E::OnMenuItemSelected if handle == app.file_stop => app.stop_selected(),
                E::OnMenuItemSelected if handle == app.file_delete => app.delete_selected(),
                E::OnMenuItemSelected if handle == app.system_open_recordings => {
                    app.open_recordings_folder()
                }
                E::OnMenuItemSelected if handle == app.system_open_settings => {
                    app.open_settings_folder()
                }
                E::OnMenuItemSelected if handle == app.file_settings => app.open_settings_dialog(),
                E::OnMenuItemSelected if handle == app.file_exit => app.window.close(),
                E::OnMenuItemSelected if handle == app.help_updates => app.check_updates(),
                E::OnMenuItemSelected if handle == app.help_about => app.about(),
                E::OnMenuItemSelected if handle == app.popup_add => app.open_new_station_dialog(),
                E::OnMenuItemSelected if handle == app.popup_edit => {
                    app.open_selected_station_dialog()
                }
                E::OnMenuItemSelected if handle == app.popup_start => app.start_selected(),
                E::OnMenuItemSelected if handle == app.popup_stop => app.stop_selected(),
                E::OnMenuItemSelected if handle == app.popup_delete => app.delete_selected(),
                E::OnContextMenu if handle == app.tray => app.show_tray_menu(),
                E::OnContextMenu if handle == app.station_list => {
                    app.show_station_menu_at_keyboard_anchor()
                }
                E::OnContextMenu if handle == app.window && app.station_list.focus() => {
                    app.show_station_menu_at_keyboard_anchor()
                }
                E::OnTimerTick if handle == app.timer => app.refresh_ui(),
                E::OnListViewItemChanged if handle == app.station_list => {
                    app.on_station_selected(&evt_data)
                }
                E::OnListViewRightClick if handle == app.station_list => {
                    app.on_station_right_click(&evt_data)
                }
                E::OnListViewItemActivated if handle == app.station_list => {
                    app.open_selected_station_dialog()
                }
                _ => {}
            }
        });
    *ui.inner.handler.borrow_mut() = Some(handler);
}

fn bind_station_events(ui: &MainWindowUi) {
    use nwg::Event as E;
    let weak = Rc::downgrade(&ui.inner);
    let handler = nwg::full_bind_event_handler(
        &ui.inner.station_window.handle,
        move |evt, evt_data, handle| {
            let Some(app) = weak.upgrade() else {
                return;
            };
            match evt {
                E::OnWindowClose if handle == app.station_window => {
                    app.on_station_window_close(&evt_data)
                }
                E::OnButtonClick if handle == app.station_ok => app.save_station(),
                E::OnButtonClick if handle == app.station_cancel => app.hide_station_dialog(),
                _ => {}
            }
        },
    );
    *ui.inner.station_handler.borrow_mut() = Some(handler);
}

fn bind_settings_events(ui: &MainWindowUi) {
    use nwg::Event as E;
    let weak = Rc::downgrade(&ui.inner);
    let handler = nwg::full_bind_event_handler(
        &ui.inner.settings_window.handle,
        move |evt, evt_data, handle| {
            let Some(app) = weak.upgrade() else {
                return;
            };
            match evt {
                E::OnWindowClose if handle == app.settings_window => {
                    app.on_settings_window_close(&evt_data)
                }
                E::OnButtonClick if handle == app.settings_recordings_folder_browse => {
                    app.browse_recordings_folder()
                }
                E::OnButtonClick if handle == app.settings_save => app.save_settings_dialog(),
                E::OnButtonClick if handle == app.settings_cancel => app.hide_settings_dialog(),
                _ => {}
            }
        },
    );
    *ui.inner.settings_handler.borrow_mut() = Some(handler);
}

fn bind_log_events(ui: &MainWindowUi) {
    use nwg::Event as E;
    let weak = Rc::downgrade(&ui.inner);
    let handler =
        nwg::full_bind_event_handler(&ui.inner.log_window.handle, move |evt, evt_data, handle| {
            let Some(app) = weak.upgrade() else {
                return;
            };
            match evt {
                E::OnWindowClose if handle == app.log_window => app.on_log_window_close(&evt_data),
                E::OnButtonClick if handle == app.log_close => app.hide_log_window(),
                _ => {}
            }
        });
    *ui.inner.log_handler.borrow_mut() = Some(handler);
}

fn bind_tray_events(ui: &MainWindowUi) {
    use nwg::Event as E;
    let weak = Rc::downgrade(&ui.inner);
    let handler = nwg::full_bind_event_handler(
        &ui.inner.tray_window.handle,
        move |evt, _evt_data, handle| {
            let Some(app) = weak.upgrade() else {
                return;
            };
            match evt {
                E::OnMenuItemSelected if handle == app.tray_show => app.show_from_tray(),
                E::OnMenuItemSelected if handle == app.tray_settings => {
                    app.show_from_tray();
                    app.open_settings_dialog();
                }
                E::OnMenuItemSelected if handle == app.tray_exit => app.window.close(),
                E::OnContextMenu if handle == app.tray => app.show_tray_menu(),
                E::OnMousePress(nwg::MousePressEvent::MousePressLeftUp) if handle == app.tray => {
                    app.show_from_tray()
                }
                _ => {}
            }
        },
    );
    *ui.inner.tray_handler.borrow_mut() = Some(handler);
}

fn bind_raw_handlers(ui: &MainWindowUi) {
    let weak = Rc::downgrade(&ui.inner);
    if let Ok(handler) = nwg::bind_raw_event_handler(
        &ui.inner.station_list.handle,
        0x10010,
        move |_hwnd, msg, _w, l| {
            let Some(app) = weak.upgrade() else {
                return None;
            };
            if msg == WM_CONTEXTMENU && is_keyboard_context_menu(l) {
                app.show_station_menu_at_keyboard_anchor();
                return Some(0);
            }
            None
        },
    ) {
        ui.inner.raw_handlers.borrow_mut().push(handler);
    }

    let weak = Rc::downgrade(&ui.inner);
    if let Ok(handler) = nwg::bind_raw_event_handler(
        &ui.inner.window.handle,
        0x10011,
        move |_hwnd, msg, _w, l| {
            let Some(app) = weak.upgrade() else {
                return None;
            };
            if msg == WM_CONTEXTMENU && is_keyboard_context_menu(l) && app.station_list.focus() {
                app.show_station_menu_at_keyboard_anchor();
                return Some(0);
            }
            None
        },
    ) {
        ui.inner.raw_handlers.borrow_mut().push(handler);
    }
}

impl MainWindow {
    fn setup(&self) {
        self.log_window.set_visible(false);
        self.station_tabs.set_selected_tab(0);
        self.update_show_log_button();
        self.apply_saved_runtime_settings();
        self.refresh_ui();
        self.station_list.set_focus();

        let settings = app_context().settings_snapshot();
        if settings.start_minimized {
            self.window.minimize();
            if settings.minimize_to_tray {
                self.window.set_visible(false);
            }
        }
    }

    fn refresh_ui(&self) {
        self.refresh_station_list();
        self.refresh_logs();
        self.refresh_status();
    }

    fn refresh_station_list(&self) {
        let stations = app_context().stations_snapshot();
        let selected_id = self.selected_station_id_silent();
        let snapshots = app_context().recorder.snapshots();
        let expected_rows = stations
            .iter()
            .map(|station| station.id)
            .collect::<Vec<_>>();
        let needs_rebuild = *self.station_rows.borrow() != expected_rows;

        self.station_list.set_redraw(false);
        if needs_rebuild {
            self.station_list.clear();
            self.station_rows.borrow_mut().clear();
        }

        for (index, station) in stations.iter().enumerate() {
            let row = station_list_row(station, snapshots.get(&station.id));
            if needs_rebuild {
                self.station_list.insert_items_row(None, &row);
                self.station_rows.borrow_mut().push(station.id);
            } else {
                for (column, value) in row.iter().enumerate() {
                    self.station_list.update_item(
                        index,
                        nwg::InsertListViewItem {
                            column_index: column as i32,
                            text: Some(value.clone()),
                            ..Default::default()
                        },
                    );
                }
            }
        }

        let mut next_selected = selected_id;
        if needs_rebuild {
            next_selected = None;
            if let Some(selected_id) = selected_id {
                if let Some(index) = self
                    .station_rows
                    .borrow()
                    .iter()
                    .position(|value| *value == selected_id)
                {
                    self.station_list.select_item(index, true);
                    next_selected = Some(selected_id);
                }
            }

            if next_selected.is_none() {
                next_selected = self.station_rows.borrow().first().copied();
                if next_selected.is_some() {
                    self.station_list.select_item(0, true);
                }
            }
        } else if next_selected.is_none() {
            next_selected = self.station_rows.borrow().first().copied();
        }

        *self.current_station_id.borrow_mut() = next_selected;

        self.station_list.set_redraw(true);
        self.station_list.invalidate();
        self.update_station_actions();
    }

    fn refresh_logs(&self) {
        let text = app_context().logs.entries_text();
        if *self.last_log_text.borrow() != text {
            let lines = text
                .lines()
                .map(|line| line.to_string())
                .collect::<Vec<_>>();
            self.log_box.set_collection(lines);
            if *self.log_visible.borrow() && self.log_box.len() > 0 {
                self.log_box.set_selection(Some(self.log_box.len() - 1));
            }
            *self.last_log_text.borrow_mut() = text;
        }
    }

    fn refresh_status(&self) {
        let active = app_context()
            .stations_snapshot()
            .iter()
            .filter(|station| app_context().recorder.is_recording(station.id))
            .count();
        self.status_bar
            .set_text(&format!("{} {}", tr("Currently recording:"), active));
        self.window
            .set_text(&format!("StreamRecorder - {} {}", tr("recording:"), active));
        self.tray.set_tip(&format!(
            "StreamRecorder - {} {}",
            tr("Currently recording:"),
            active
        ));
        self.update_station_actions();
    }

    fn open_new_station_dialog(&self) {
        *self.station_editor_station_id.borrow_mut() = None;
        self.station_window.set_text(tr("Add station"));
        self.populate_station_dialog(None, None);
        self.show_station_dialog();
    }

    fn open_selected_station_dialog(&self) {
        let Some(station_id) = self.selected_station_id() else {
            return;
        };
        let Some(station) = app_context().station(station_id) else {
            return;
        };
        *self.station_editor_station_id.borrow_mut() = Some(station_id);
        self.station_window.set_text(tr("Edit station"));
        self.populate_station_dialog(Some(&station), station.schedules.first());
        self.show_station_dialog();
    }

    fn show_station_dialog(&self) {
        let (x, y) = self.window.position();
        self.station_window.set_position(x + 40, y + 40);
        self.station_tabs.set_selected_tab(0);
        self.window.set_enabled(false);
        self.station_window.set_visible(true);
        self.station_window.restore();
        self.station_tabs.set_focus();
    }

    fn hide_station_dialog(&self) {
        *self.station_editor_station_id.borrow_mut() = None;
        self.station_window.set_visible(false);
        self.station_tabs.set_selected_tab(0);
        self.window.set_enabled(true);
        self.window.set_focus();
        self.station_list.set_focus();
    }

    fn populate_station_dialog(&self, station: Option<&Station>, schedule: Option<&ScheduleRule>) {
        if let Some(station) = station {
            self.name_input.set_text(&station.name);
            self.url_input.set_text(&station.url);
            if let Some(credentials) = &station.credentials {
                self.user_input.set_text(&credentials.username);
                self.pass_input.set_text(&credentials.password);
            } else {
                self.user_input.set_text("");
                self.pass_input.set_text("");
            }
        } else {
            self.name_input.set_text("");
            self.url_input.set_text("");
            self.user_input.set_text("");
            self.pass_input.set_text("");
        }
        self.populate_schedule_controls(schedule);
    }

    fn save_station(&self) {
        match self.station_from_form() {
            Ok(station) => {
                let id = station.id;
                let name = station.name.clone();
                if let Err(error) = app_context().upsert_station(station) {
                    nwg::modal_error_message(&self.station_window, tr("Error"), &error.to_string());
                    return;
                }
                self.hide_station_dialog();
                *self.current_station_id.borrow_mut() = Some(id);
                app_context()
                    .logs
                    .push(format!("{}: {}", tr("Saved station"), name));
                self.refresh_station_list();
                self.focus_station(id);
            }
            Err(error) => {
                self.focus_station_dialog_for_error(&error);
                nwg::modal_error_message(&self.station_window, tr("Invalid data"), &error);
            }
        }
    }

    fn populate_schedule_controls(&self, schedule: Option<&ScheduleRule>) {
        if let Some(rule) = schedule {
            self.schedule_enabled.set_check_state(check(rule.enabled));
            set_day_checks(self, &rule.weekdays);
            self.start_input
                .set_text(&format!("{:02}:{:02}", rule.start_hour, rule.start_minute));
            self.end_input
                .set_text(&format!("{:02}:{:02}", rule.end_hour, rule.end_minute));
        } else {
            self.schedule_enabled
                .set_check_state(nwg::CheckBoxState::Unchecked);
            set_day_checks(self, &[true, true, true, true, true, true, true]);
            self.start_input.set_text("00:00");
            self.end_input.set_text("23:59");
        }
    }

    fn focus_station_dialog_for_error(&self, error: &str) {
        let schedule_errors = [
            tr("Select at least one weekday for the schedule."),
            tr("Time must use HH:MM format."),
            tr("Invalid hour value."),
            tr("Invalid minute value."),
            tr("Time is out of range."),
        ];

        if schedule_errors.iter().any(|message| *message == error) {
            self.station_tabs.set_selected_tab(1);
            self.schedule_enabled.set_focus();
        } else {
            self.station_tabs.set_selected_tab(0);
            self.name_input.set_focus();
        }
    }

    fn delete_selected(&self) {
        let Some(station_id) = self.selected_station_id() else {
            return;
        };
        let Some(station) = app_context().station(station_id) else {
            return;
        };
        let answer = nwg::modal_message(
            &self.window,
            &nwg::MessageParams {
                title: tr("Delete station"),
                content: &format!("{} \"{}\"?", tr("Delete station prompt"), station.name),
                buttons: nwg::MessageButtons::YesNo,
                icons: nwg::MessageIcons::Question,
            },
        );
        if answer == nwg::MessageChoice::Yes {
            if let Err(error) = app_context().remove_station(station_id) {
                nwg::modal_error_message(&self.window, tr("Error"), &error.to_string());
                return;
            }
            if *self.current_station_id.borrow() == Some(station_id) {
                *self.current_station_id.borrow_mut() = None;
            }
            self.refresh_ui();
        }
    }

    fn start_selected(&self) {
        let Some(station_id) = self.selected_station_id() else {
            return;
        };
        if let Err(error) = app_context().start_station(station_id) {
            nwg::modal_error_message(&self.window, tr("Recording error"), &error.to_string());
            return;
        }
        self.update_station_actions();
    }

    fn stop_selected(&self) {
        let Some(station_id) = self.selected_station_id() else {
            return;
        };
        app_context().stop_station(station_id);
        self.update_station_actions();
    }

    fn on_station_selected(&self, data: &nwg::EventData) {
        let (row_index, _, selected) = data.on_list_view_item_changed();
        if row_index == usize::MAX {
            return;
        }
        if selected {
            if let Some(station_id) = self.station_rows.borrow().get(row_index).copied() {
                self.focus_station(station_id);
            }
        } else if self.station_list.selected_item().is_none() {
            *self.current_station_id.borrow_mut() = None;
            self.update_station_actions();
        }
    }

    fn on_station_right_click(&self, data: &nwg::EventData) {
        let (row_index, _) = data.on_list_view_item_index();
        if row_index != usize::MAX {
            self.station_list.select_item(row_index, true);
            if let Some(station_id) = self.station_rows.borrow().get(row_index).copied() {
                self.focus_station(station_id);
            }
        }
        self.show_station_menu_at_cursor();
    }

    fn focus_station(&self, station_id: Uuid) {
        *self.current_station_id.borrow_mut() = Some(station_id);
        self.update_station_actions();
    }

    fn show_station_menu_at_cursor(&self) {
        self.update_station_actions();
        let (x, y) = nwg::GlobalCursor::position();
        self.show_station_menu_at(x, y);
    }

    fn show_station_menu_at_keyboard_anchor(&self) {
        self.update_station_actions();
        let (x, y) = self.station_menu_anchor();
        self.show_station_menu_at(x, y);
    }

    fn show_station_menu_at(&self, x: i32, y: i32) {
        self.popup_menu.popup(x, y);
    }

    fn station_menu_anchor(&self) -> (i32, i32) {
        let (window_x, window_y) = self.window.position();
        let (list_x, list_y) = self.station_list.position();
        let offset_y = self
            .station_list
            .selected_item()
            .map(|row| 32 + (row as i32).saturating_mul(22))
            .unwrap_or(24);
        (window_x + list_x + 24, window_y + list_y + offset_y)
    }

    fn station_from_form(&self) -> Result<Station, String> {
        let name = self.name_input.text().trim().to_string();
        let url = self.url_input.text().trim().to_string();
        if name.is_empty() {
            return Err(tr("Station name cannot be empty.").to_string());
        }
        if url.is_empty() || url::Url::parse(&url).is_err() {
            return Err(tr("The stream URL is not valid.").to_string());
        }

        let station_id = *self.station_editor_station_id.borrow();
        let schedules = self.schedules_from_dialog()?;

        Ok(Station {
            id: station_id.unwrap_or_else(Uuid::new_v4),
            name,
            url,
            credentials: {
                let username = self.user_input.text().trim().to_string();
                let password = self.pass_input.text();
                if username.is_empty() && password.trim().is_empty() {
                    None
                } else {
                    Some(Credentials { username, password })
                }
            },
            schedules,
        })
    }

    fn schedules_from_dialog(&self) -> Result<Vec<ScheduleRule>, String> {
        if !is_checked(&self.schedule_enabled) {
            return Ok(Vec::new());
        }

        let weekdays = day_checks(self);
        if !weekdays.iter().any(|value| *value) {
            return Err(tr("Select at least one weekday for the schedule.").to_string());
        }

        let (start_hour, start_minute) = parse_time_value(&self.start_input.text())?;
        let (end_hour, end_minute) = parse_time_value(&self.end_input.text())?;
        Ok(vec![ScheduleRule {
            enabled: true,
            weekdays,
            start_hour,
            start_minute,
            end_hour,
            end_minute,
        }])
    }

    fn toggle_log(&self) {
        if *self.log_visible.borrow() {
            self.hide_log_window();
        } else {
            self.show_log_window();
        }
    }

    fn update_show_log_button(&self) {
        self.show_log_button
            .set_text(if *self.log_visible.borrow() {
                tr("Hide log")
            } else {
                tr("Show log")
            });
    }

    fn show_log_window(&self) {
        let (window_x, window_y) = self.window.position();
        self.log_window.set_position(window_x + 30, window_y + 30);
        *self.log_visible.borrow_mut() = true;
        self.update_show_log_button();
        self.log_window.set_visible(true);
        self.log_window.restore();
        self.log_window.set_focus();
        if self.log_box.len() > 0 {
            self.log_box.set_selection(Some(self.log_box.len() - 1));
        }
        self.log_box.set_focus();
    }

    fn hide_log_window(&self) {
        *self.log_visible.borrow_mut() = false;
        self.log_window.set_visible(false);
        self.update_show_log_button();
        if self.window.visible() {
            self.window.set_focus();
            self.show_log_button.set_focus();
        }
    }

    fn update_station_actions(&self) {
        let selected_station = self.selected_station_id_silent();
        let recording = selected_station
            .map(|station_id| app_context().recorder.is_recording(station_id))
            .unwrap_or(false);
        let has_selection = selected_station.is_some();

        self.file_edit.set_enabled(has_selection);
        self.file_start.set_enabled(has_selection && !recording);
        self.file_stop.set_enabled(has_selection && recording);
        self.file_delete.set_enabled(has_selection);

        self.popup_edit.set_enabled(has_selection);
        self.popup_start.set_enabled(has_selection && !recording);
        self.popup_stop.set_enabled(has_selection && recording);
        self.popup_delete.set_enabled(has_selection);
    }

    fn on_station_window_close(&self, data: &nwg::EventData) {
        if let nwg::EventData::OnWindowClose(close_data) = data {
            close_data.close(false);
        }
        *self.station_editor_station_id.borrow_mut() = None;
        self.hide_station_dialog();
    }

    fn open_settings_dialog(&self) {
        self.populate_settings_dialog();
        let (x, y) = self.window.position();
        self.settings_window.set_position(x + 40, y + 40);
        self.window.set_enabled(false);
        self.settings_window.set_visible(true);
        self.settings_window.restore();
        self.settings_window.set_focus();
        self.settings_recordings_folder_input.set_focus();
    }

    fn hide_settings_dialog(&self) {
        self.settings_window.set_visible(false);
        self.window.set_enabled(true);
        self.window.set_focus();
        self.station_list.set_focus();
    }

    fn populate_settings_dialog(&self) {
        let settings = app_context().settings_snapshot();
        self.settings_launch_on_startup
            .set_check_state(check(settings.launch_on_startup));
        self.settings_always_on_top
            .set_check_state(check(settings.always_on_top));
        self.settings_minimize_to_tray
            .set_check_state(check(settings.minimize_to_tray));
        self.settings_confirm_on_exit
            .set_check_state(check(settings.confirm_on_exit));
        self.settings_restart_on_crash
            .set_check_state(check(settings.restart_on_crash));
        self.settings_prevent_sleep
            .set_check_state(check(settings.prevent_sleep));
        self.settings_start_minimized
            .set_check_state(check(settings.start_minimized));
        self.settings_recordings_folder_input
            .set_text(&settings.recordings_folder.to_string_lossy());
        self.settings_template_input
            .set_text(&settings.file_name_template);
        self.settings_remux_raw_aac
            .set_check_state(check(settings.remux_raw_aac_to_m4a));
        self.settings_language_combo
            .set_selection(Some(language_selection_index_for(&settings.language)));
        self.settings_update_repo_input
            .set_text(&settings.update_repo);
    }

    fn save_settings_dialog(&self) {
        let previous = app_context().settings_snapshot();
        let settings = match self.settings_from_dialog(&previous) {
            Ok(settings) => settings,
            Err(error) => {
                nwg::modal_error_message(&self.settings_window, tr("Settings error"), &error);
                return;
            }
        };

        if let Err(error) = app_context().save_settings(settings.clone()) {
            nwg::modal_error_message(
                &self.settings_window,
                tr("Settings error"),
                &error.to_string(),
            );
            return;
        }

        self.apply_runtime_settings(&settings);
        self.hide_settings_dialog();
        self.refresh_ui();
        app_context().logs.push(tr("Application settings saved"));

        if previous.restart_on_crash != settings.restart_on_crash {
            nwg::modal_info_message(
                &self.window,
                tr("Crash monitor"),
                if settings.restart_on_crash {
                    tr("Crash monitoring will be activated on the next program start.")
                } else {
                    tr(
                        "Disabling crash monitoring will be fully applied after restarting the program.",
                    )
                },
            );
        }

        if previous.language != settings.language {
            nwg::modal_info_message(
                &self.window,
                tr("Restart required"),
                tr("Language changes will be fully applied after restarting the program."),
            );
        }
    }

    fn settings_from_dialog(&self, previous: &AppSettings) -> Result<AppSettings, String> {
        let recordings_folder = {
            let text = self.settings_recordings_folder_input.text();
            let trimmed = text.trim();
            if trimmed.is_empty() {
                PathBuf::from("My recordings")
            } else {
                PathBuf::from(trimmed)
            }
        };

        Ok(AppSettings {
            launch_on_startup: is_checked(&self.settings_launch_on_startup),
            always_on_top: is_checked(&self.settings_always_on_top),
            minimize_to_tray: is_checked(&self.settings_minimize_to_tray),
            confirm_on_exit: is_checked(&self.settings_confirm_on_exit),
            restart_on_crash: is_checked(&self.settings_restart_on_crash),
            prevent_sleep: is_checked(&self.settings_prevent_sleep),
            start_minimized: is_checked(&self.settings_start_minimized),
            remux_raw_aac_to_m4a: is_checked(&self.settings_remux_raw_aac),
            recordings_folder,
            file_name_template: self.settings_template_input.text().trim().to_string(),
            language: language_from_selection(self.settings_language_combo.selection()),
            update_repo: normalize_update_repo(&self.settings_update_repo_input.text()),
            ..previous.clone()
        })
    }

    fn browse_recordings_folder(&self) {
        let current = self.settings_recordings_folder_input.text();
        let current = current.trim();
        if !current.is_empty() {
            let path = PathBuf::from(current);
            let path = if path.is_absolute() {
                path
            } else {
                app_context().paths.root_dir.join(path)
            };
            let _ = self
                .settings_folder_dialog
                .set_default_folder(&path.to_string_lossy());
        }

        if self.settings_folder_dialog.run(Some(&self.settings_window)) {
            if let Ok(selected) = self.settings_folder_dialog.get_selected_item() {
                self.settings_recordings_folder_input
                    .set_text(&selected.to_string_lossy());
            }
        }
    }

    fn selected_station_id_silent(&self) -> Option<Uuid> {
        if let Some(station_id) = *self.current_station_id.borrow() {
            return Some(station_id);
        }

        self.station_list
            .selected_item()
            .and_then(|index| self.station_rows.borrow().get(index).copied())
    }

    fn selected_station_id(&self) -> Option<Uuid> {
        let selected = self.selected_station_id_silent();
        if selected.is_none() {
            nwg::modal_info_message(
                &self.window,
                tr("No station selected"),
                tr("Select a station from the list or add a new one."),
            );
        }
        selected
    }

    fn open_recordings_folder(&self) {
        let settings = app_context().settings_snapshot();
        let path = if settings.recordings_folder.is_absolute() {
            settings.recordings_folder
        } else {
            app_context()
                .paths
                .root_dir
                .join(settings.recordings_folder)
        };
        open_target(&path);
    }

    fn open_settings_folder(&self) {
        open_target(&app_context().paths.config_dir);
    }

    fn check_updates(&self) {
        let repo = app_context().settings_snapshot().update_repo;
        if repo.trim().is_empty() {
            nwg::modal_info_message(
                &self.window,
                tr("Updates"),
                tr("Configure the GitHub repository in Settings to check for updates."),
            );
            return;
        }
        match app_context().check_for_updates() {
            Ok(Some(update)) => {
                if let Some(asset) = update.asset.clone() {
                    let answer = nwg::modal_message(
                        &self.window,
                        &nwg::MessageParams {
                            title: tr("Update available"),
                            content: &format!(
                                "{} {}\n{}: {} ({} KB)\n\n{}",
                                tr("Available version:"),
                                update.version,
                                tr("Downloadable asset"),
                                asset.name,
                                asset.size / 1024,
                                tr("Download and install the update now?")
                            ),
                            buttons: nwg::MessageButtons::YesNo,
                            icons: nwg::MessageIcons::Info,
                        },
                    );
                    if answer != nwg::MessageChoice::Yes {
                        return;
                    }

                    app_context().logs.push(format!(
                        "{}: {}",
                        tr("Downloading update"),
                        asset.name
                    ));

                    match download_update(&app_context().paths, &update) {
                        Ok(downloaded_path) => {
                            let restart_exe = match std::env::current_exe() {
                                Ok(path) => path,
                                Err(error) => {
                                    nwg::modal_error_message(
                                        &self.window,
                                        tr("Updates"),
                                        &error.to_string(),
                                    );
                                    return;
                                }
                            };
                            let restart_args = std::env::args()
                                .skip(1)
                                .filter(|arg| arg != GUARDED_ARG)
                                .collect::<Vec<_>>();

                            if let Err(error) = install_downloaded_update(
                                &app_context().paths,
                                &downloaded_path,
                                &asset,
                                &restart_exe,
                                &restart_args,
                            ) {
                                nwg::modal_error_message(
                                    &self.window,
                                    tr("Updates"),
                                    &error.to_string(),
                                );
                                return;
                            }

                            nwg::modal_info_message(
                                &self.window,
                                tr("Updates"),
                                tr(
                                    "The update has been downloaded. StreamRecorder will now close and install the update.",
                                ),
                            );
                            self.exit_for_update();
                        }
                        Err(error) => {
                            nwg::modal_error_message(
                                &self.window,
                                tr("Updates"),
                                &format!("{}: {}", tr("Failed to download the update"), error),
                            );
                        }
                    }
                } else {
                    let answer = nwg::modal_message(
                        &self.window,
                        &nwg::MessageParams {
                            title: tr("Update available"),
                            content: &format!(
                                "{} {}\n{}\n\n{}",
                                tr("Available version:"),
                                update.version,
                                update.html_url,
                                tr(
                                    "No supported downloadable asset was found. Open the release page in your browser?"
                                )
                            ),
                            buttons: nwg::MessageButtons::YesNo,
                            icons: nwg::MessageIcons::Info,
                        },
                    );
                    if answer == nwg::MessageChoice::Yes {
                        open_url(&update.html_url);
                    }
                }
            }
            Ok(None) => {
                nwg::modal_info_message(
                    &self.window,
                    tr("Updates"),
                    tr("No newer version is available."),
                );
            }
            Err(error) => {
                nwg::modal_error_message(&self.window, tr("Updates"), &error.to_string());
            }
        };
    }

    fn about(&self) {
        nwg::modal_info_message(
            &self.window,
            tr("About"),
            &format!(
                "StreamRecorder {}\n{}",
                env!("CARGO_PKG_VERSION"),
                tr("Portable audio stream recorder.")
            ),
        );
    }

    fn show_tray_menu(&self) {
        let (x, y) = nwg::GlobalCursor::position();
        self.tray_menu.popup(x, y);
    }

    fn show_from_tray(&self) {
        self.window.restore();
        self.window.set_visible(true);
        activate_window(&self.window);
        self.window.set_focus();
        self.station_list.set_focus();
    }

    fn on_window_minimize(&self) {
        if app_context().settings_snapshot().minimize_to_tray {
            self.log_window.set_visible(false);
            self.window.set_visible(false);
            self.tray.set_focus();
        }
    }

    fn on_window_close(&self, data: &nwg::EventData) {
        self.log_window.set_visible(false);
        self.station_window.set_visible(false);
        self.settings_window.set_visible(false);
        if app_context().settings_snapshot().confirm_on_exit {
            let answer = nwg::modal_message(
                &self.window,
                &nwg::MessageParams {
                    title: tr("Close StreamRecorder"),
                    content: tr("Do you really want to close StreamRecorder?"),
                    buttons: nwg::MessageButtons::YesNo,
                    icons: nwg::MessageIcons::Question,
                },
            );
            if answer != nwg::MessageChoice::Yes {
                if let nwg::EventData::OnWindowClose(close_data) = data {
                    close_data.close(false);
                }
                return;
            }
        }
        app_context().shutdown();
        nwg::stop_thread_dispatch();
    }

    fn exit_for_update(&self) {
        self.log_window.set_visible(false);
        self.station_window.set_visible(false);
        self.settings_window.set_visible(false);
        self.window.set_visible(false);
        app_context().shutdown();
        nwg::stop_thread_dispatch();
    }

    fn on_settings_window_close(&self, data: &nwg::EventData) {
        if let nwg::EventData::OnWindowClose(close_data) = data {
            close_data.close(false);
        }
        self.hide_settings_dialog();
    }

    fn on_log_window_close(&self, data: &nwg::EventData) {
        if let nwg::EventData::OnWindowClose(close_data) = data {
            close_data.close(false);
        }
        self.hide_log_window();
    }

    fn apply_saved_runtime_settings(&self) {
        let settings = app_context().settings_snapshot();
        self.apply_runtime_settings(&settings);
    }

    fn apply_runtime_settings(&self, settings: &AppSettings) {
        set_topmost(&self.window, settings.always_on_top);
        if let Err(error) = set_launch_on_startup(settings.launch_on_startup) {
            app_context().logs.push(format!(
                "{}: {}",
                tr("Failed to update startup registration"),
                error
            ));
        }
        set_sleep_prevention(settings.prevent_sleep);
    }
}

impl Drop for MainWindowUi {
    fn drop(&mut self) {
        if let Some(handler) = self.inner.handler.borrow_mut().take() {
            nwg::unbind_event_handler(&handler);
        }
        if let Some(handler) = self.inner.station_handler.borrow_mut().take() {
            nwg::unbind_event_handler(&handler);
        }
        if let Some(handler) = self.inner.settings_handler.borrow_mut().take() {
            nwg::unbind_event_handler(&handler);
        }
        if let Some(handler) = self.inner.log_handler.borrow_mut().take() {
            nwg::unbind_event_handler(&handler);
        }
        if let Some(handler) = self.inner.tray_handler.borrow_mut().take() {
            nwg::unbind_event_handler(&handler);
        }
        for handler in self.inner.raw_handlers.borrow_mut().drain(..) {
            let _ = nwg::unbind_raw_event_handler(&handler);
        }
    }
}

fn is_keyboard_context_menu(lparam: isize) -> bool {
    lparam == -1
}

fn check(value: bool) -> nwg::CheckBoxState {
    if value {
        nwg::CheckBoxState::Checked
    } else {
        nwg::CheckBoxState::Unchecked
    }
}

fn is_checked(control: &nwg::CheckBox) -> bool {
    control.check_state() == nwg::CheckBoxState::Checked
}

fn day_checks(ui: &MainWindow) -> [bool; 7] {
    [
        is_checked(&ui.day_mon),
        is_checked(&ui.day_tue),
        is_checked(&ui.day_wed),
        is_checked(&ui.day_thu),
        is_checked(&ui.day_fri),
        is_checked(&ui.day_sat),
        is_checked(&ui.day_sun),
    ]
}

fn set_day_checks(ui: &MainWindow, values: &[bool; 7]) {
    ui.day_mon.set_check_state(check(values[0]));
    ui.day_tue.set_check_state(check(values[1]));
    ui.day_wed.set_check_state(check(values[2]));
    ui.day_thu.set_check_state(check(values[3]));
    ui.day_fri.set_check_state(check(values[4]));
    ui.day_sat.set_check_state(check(values[5]));
    ui.day_sun.set_check_state(check(values[6]));
}

fn parse_time_value(value: &str) -> Result<(u8, u8), String> {
    let Some((hour, minute)) = value.trim().split_once(':') else {
        return Err(tr("Time must use HH:MM format.").to_string());
    };
    let hour = hour
        .parse::<u8>()
        .map_err(|_| tr("Invalid hour value.").to_string())?;
    let minute = minute
        .parse::<u8>()
        .map_err(|_| tr("Invalid minute value.").to_string())?;
    if hour > 23 || minute > 59 {
        return Err(tr("Time is out of range.").to_string());
    }
    Ok((hour, minute))
}

fn display_state_label(label: &str) -> String {
    if let Some(format) = label.strip_prefix("Recording ") {
        return format!("{} {}", tr("Recording"), format);
    }
    if let Some(message) = label.strip_prefix("Error: ") {
        return format!("{}: {}", tr("Error"), message);
    }

    match label {
        "Idle" => tr("Idle").to_string(),
        "Connecting" => tr("Connecting").to_string(),
        "Reconnecting" => tr("Reconnecting").to_string(),
        "Waiting for reconnect" => tr("Waiting for reconnect").to_string(),
        "Waiting for playlist" => tr("Waiting for playlist").to_string(),
        "Waiting for HLS segments" => tr("Waiting for HLS segments").to_string(),
        "Stopping" => tr("Stopping").to_string(),
        "Stopped" => tr("Stopped").to_string(),
        _ => label.to_string(),
    }
}

fn station_list_row(station: &Station, snapshot: Option<&RecordingSnapshot>) -> Vec<String> {
    let status = snapshot
        .map(|value| display_state_label(&value.state_label))
        .unwrap_or_else(|| tr("Idle").to_string());
    let format = snapshot
        .and_then(|value| value.format)
        .map(|value| value.display_name().to_string())
        .unwrap_or_else(|| "-".to_string());
    let output = snapshot
        .and_then(|value| value.output_path.as_ref())
        .and_then(|path| path.file_name())
        .and_then(|value| value.to_str())
        .unwrap_or("-")
        .to_string();

    vec![
        station.name.clone(),
        station.url.clone(),
        status,
        format,
        output,
    ]
}

fn language_options() -> Vec<String> {
    vec![tr("Polish").to_string(), tr("English").to_string()]
}

fn language_selection_index() -> usize {
    language_selection_index_for(&current_language())
}

fn language_selection_index_for(language: &streamrecorder::models::Language) -> usize {
    match language {
        streamrecorder::models::Language::Polish => 0,
        streamrecorder::models::Language::English => 1,
    }
}

fn language_from_selection(index: Option<usize>) -> streamrecorder::models::Language {
    match index {
        Some(1) => streamrecorder::models::Language::English,
        _ => streamrecorder::models::Language::Polish,
    }
}

fn open_target(path: &Path) {
    let _ = Command::new("explorer.exe").arg(path).spawn();
}

fn open_url(url: &str) {
    let _ = Command::new("explorer.exe").arg(url).spawn();
}

fn normalize_update_repo(value: &str) -> String {
    let trimmed = value.trim().trim_matches('/');
    if trimmed.is_empty() {
        return String::new();
    }

    for prefix in ["https://github.com/", "http://github.com/", "github.com/"] {
        if let Some(rest) = trimmed.strip_prefix(prefix) {
            return take_repo_segments(rest).unwrap_or_else(|| trimmed.to_string());
        }
    }

    take_repo_segments(trimmed).unwrap_or_else(|| trimmed.to_string())
}

fn take_repo_segments(value: &str) -> Option<String> {
    let mut parts = value.split('/').filter(|part| !part.is_empty());
    let owner = parts.next()?;
    let repo = parts.next()?.trim_end_matches(".git");
    Some(format!("{owner}/{repo}"))
}

fn set_topmost(window: &nwg::Window, topmost: bool) {
    if let Some(handle) = control_hwnd(&window.handle) {
        let target = if topmost {
            HWND_TOPMOST
        } else {
            HWND_NOTOPMOST
        };
        unsafe {
            let _ = SetWindowPos(handle, Some(target), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }
    }
}

fn control_hwnd(handle: &nwg::ControlHandle) -> Option<HWND> {
    handle.hwnd().map(|raw| HWND(raw as *mut _))
}

fn add_tab_stop_style(handle: &nwg::ControlHandle) {
    let Some(hwnd) = control_hwnd(handle) else {
        return;
    };

    unsafe {
        let style = GetWindowLongW(hwnd, GWL_STYLE) as u32;
        if style & WS_TABSTOP.0 == 0 {
            let _ = SetWindowLongW(hwnd, GWL_STYLE, (style | WS_TABSTOP.0) as i32);
        }
    }
}

fn activate_window(window: &nwg::Window) {
    let Some(handle) = control_hwnd(&window.handle) else {
        return;
    };

    unsafe {
        let _ = SetForegroundWindow(handle);
        let _ = BringWindowToTop(handle);
    }
}

fn set_sleep_prevention(enabled: bool) {
    unsafe {
        if enabled {
            let _ =
                SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
        } else {
            let _ = SetThreadExecutionState(ES_CONTINUOUS);
        }
    }
}

fn set_launch_on_startup(enabled: bool) -> std::io::Result<()> {
    let key = r"HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
    if enabled {
        let exe = std::env::current_exe()?;
        let exe_value = format!("\"{}\"", exe.display());
        let status = Command::new("reg")
            .args([
                "add",
                key,
                "/v",
                "StreamRecorder",
                "/t",
                "REG_SZ",
                "/d",
                &exe_value,
                "/f",
            ])
            .status()?;
        if status.success() {
            Ok(())
        } else {
            Err(std::io::Error::other("registry add command failed"))
        }
    } else {
        let query = Command::new("reg")
            .args(["query", key, "/v", "StreamRecorder"])
            .status()?;
        if !query.success() {
            return Ok(());
        }

        let status = Command::new("reg")
            .args(["delete", key, "/v", "StreamRecorder", "/f"])
            .status()?;
        if status.success() {
            Ok(())
        } else {
            Err(std::io::Error::other("registry delete command failed"))
        }
    }
}

fn is_guarded_process() -> bool {
    std::env::args().skip(1).any(|arg| arg == GUARDED_ARG)
}

fn relaunch_under_guard_if_needed() -> anyhow::Result<bool> {
    if is_guarded_process() {
        return Ok(false);
    }

    let paths = AppPaths::discover()?;
    let config = load_or_create(&paths)?;
    if !config.settings.restart_on_crash {
        return Ok(false);
    }

    let current_exe = std::env::current_exe()?;
    let guard_exe = current_exe
        .parent()
        .map(|parent| parent.join("streamrecorder_guard.exe"))
        .unwrap_or_else(|| PathBuf::from("streamrecorder_guard.exe"));
    if !guard_exe.exists() {
        return Ok(false);
    }

    let mut command = Command::new(guard_exe);
    command
        .arg("--exe")
        .arg(&current_exe)
        .arg("--")
        .arg(GUARDED_ARG);
    for arg in std::env::args().skip(1) {
        if arg != GUARDED_ARG {
            command.arg(arg);
        }
    }
    command.spawn()?;
    Ok(true)
}

fn main() {
    if relaunch_under_guard_if_needed().unwrap_or(false) {
        return;
    }

    nwg::init().expect("failed to initialize NWG");
    nwg::Font::set_global_family("Segoe UI").expect("failed to set default font");

    let context = AppContext::load().unwrap_or_else(|error| {
        nwg::fatal_message(
            "StreamRecorder",
            &format!("Failed to start the application:\n{}", error),
        )
    });
    let _ = APP_CONTEXT.set(context);

    let _app = MainWindow::build_ui(Default::default()).expect("failed to build UI");
    nwg::dispatch_thread_events();
}
