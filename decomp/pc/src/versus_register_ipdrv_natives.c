#include "versus_native_registration.h"

void register_ipdrv_natives(void)
{
    REGISTER_NATIVE(1261, exec_IpDrv_UdpLink_ReadBinary);
    REGISTER_NATIVE(1260, exec_IpDrv_UdpLink_ReadText);
    REGISTER_NATIVE(1259, exec_IpDrv_UdpLink_SendBinary);
    REGISTER_NATIVE(1258, exec_IpDrv_UdpLink_SendText);
    REGISTER_NATIVE(1257, exec_IpDrv_UdpLink_BindPort);
    REGISTER_NATIVE(1256, exec_IpDrv_UdpLink_GetPlateForm);
    REGISTER_NATIVE(1255, exec_IpDrv_InternetLink_GetLocalIP);
    REGISTER_NATIVE(1254, exec_IpDrv_InternetLink_StringToIpAddr);
    REGISTER_NATIVE(1253, exec_IpDrv_InternetLink_IpAddrToString);
    REGISTER_NATIVE(1252, exec_IpDrv_InternetLink_GetLastError);
    REGISTER_NATIVE(1251, exec_IpDrv_InternetLink_Resolve);
    REGISTER_NATIVE(1250, exec_IpDrv_InternetLink_ParseURL);
    REGISTER_NATIVE(1249, exec_IpDrv_InternetLink_IsDataPending);
    REGISTER_NATIVE(1270, exec_IpDrv_TcpLink_ReadBinary);
    REGISTER_NATIVE(1269, exec_IpDrv_TcpLink_ReadText);
    REGISTER_NATIVE(1268, exec_IpDrv_TcpLink_SendBinary);
    REGISTER_NATIVE(1267, exec_IpDrv_TcpLink_SendText);
    REGISTER_NATIVE(1266, exec_IpDrv_TcpLink_IsConnected);
    REGISTER_NATIVE(1265, exec_IpDrv_TcpLink_Close);
    REGISTER_NATIVE(1264, exec_IpDrv_TcpLink_Open);
    REGISTER_NATIVE(1263, exec_IpDrv_TcpLink_Listen);
    REGISTER_NATIVE(1262, exec_IpDrv_TcpLink_BindPort);
}
