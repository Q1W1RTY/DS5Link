namespace DS5Link;

internal enum ConnectionAction { None, Diagnostics, Recover }
internal sealed record ConnectionPresentation(string Text, ConnectionAction Action = ConnectionAction.None)
{
    internal static ConnectionPresentation From(BridgeState state) => state.ReasonCode switch
    {
        "idle_timeout" => new(L.T("Отключён из-за бездействия. Нажмите PS для подключения.")),
        "manual_disconnect" when state.Enabled => new(L.T("Нажмите PS для подключения.")),
        "usb_direct" => new(L.T("DualSense доступен напрямую по USB. Для эмуляции выберите Xbox.")),
        "external_owner" => new(L.T("Закройте DSX, чтобы DS5Link мог подключить геймпад.")),
        "recovering" => new(L.T("Восстанавливаем подключение.")),
        "connecting" => new(L.T("Подключаем контроллер.")),
        "disconnecting" => new(L.T("Отключаем контроллер.")),
        "controller_unavailable" => new(L.T("Включите DualSense кнопкой PS.")),
        "dependency_unavailable" => new(L.T("Не удалось запустить виртуальный контроллер. Проверьте драйверы."), ConnectionAction.Diagnostics),
        "service_unavailable" => new(L.T("Нет связи со службой DS5Link."), ConnectionAction.Diagnostics),
        "hidhide_recovery_pending" => new(L.T("Требуется восстановление HidHide."), state.Enabled ? ConnectionAction.Diagnostics : ConnectionAction.Recover),
        "connection_error" => new(L.T("Не удалось подключить контроллер."), ConnectionAction.Diagnostics),
        _ => new("")
    };
}
