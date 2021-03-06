Imports System.Data
Imports System.IO
Imports System.Net.NetworkInformation
Imports System.Configuration
Imports System.Xml
Imports MySql.Data
Imports POSMySQL
Imports ClspRoMiSeForWeb.CallWebservice
Imports System.Threading
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Public Class Service
    Dim dbutil As New POSControl.CDBUtil()
    Dim DefaultConn As New MySqlClient.MySqlConnection()
    Private Const DATABASESETTINGNODENAME As String = "FrontDataSetting"
    Private Const MANAGEDATASETTINGNODENAME As String = "ManageDataSetting"
    Private Const CONFIGTAYWINFILE As String = "pRoMiSeFrontRes.xml"
    Private FOLDER_ERROR As String = "LogFileWebService"
    Private xErrorForm As String = "Webservice"
    Private xLogFileName As String = "DataWebService"
    Private ManageDataExchangeFolder As String
    Private MySQLDumpPath As String
    Private XMLProfile As AMS.Profile.Xml
    Private DefaultIp As String
    Private DefaultDbName As String
    Private RegionID As Integer
    Private iTime As Integer
    Private TimeSet As DateTime
    Private ctmMain As ContextMenu
    Private IPWebservice As String
    Private WithEvents mniMainProgramExit As New MenuItem
    Private WithEvents mniMainProgramRefresh As New MenuItem
    Private WithEvents mniMainProgramSetting As New MenuItem

    Dim wsThread As Thread
    Dim wsSocket As Socket
    Dim wsTcpListener As TcpListener

    Dim OnprocessFuntion As Boolean = False

    Enum GetPort
        SendDataToHQ = 5100
    End Enum
    Enum GetMethod
        ExportImportSummarySale = 1
        ExportImportPoint = 2
        ExportImportDocument = 3
        ExportImportCouponVoucher = 4
        ExportImportAllData = 5
        ExportVoucherToHQ = 6
        ExportTranferDelivery = 7
    End Enum
    Private Function Main() As Boolean
        '--------------------------------------------------------- 
        'Get current process 
        Dim current As Process = Process.GetCurrentProcess()

        'Get array or processes with same name 
        Dim procs As Process() = Process.GetProcessesByName(current.ProcessName)
        '--------------------------------------------------------------- 
        If procs.Length = 1 Then
            Return False
        Else
            Return True
        End If
    End Function
    Private Sub Service_Load(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles MyBase.Load
        Try
            If Main() = True Then
                Application.ExitThread()
                Application.Exit()
                Exit Sub
            End If
            Me.Hide()
            NotifyIcon1.Visible = True
            SetMainNotify()
            XMLProfile = New AMS.Profile.Xml
            XMLProfile.Name = Application.StartupPath & "/" & CONFIGTAYWINFILE
            RefreshAllRetailFrontXMLData()
            LoadManageDataSettingConfigFromXMLFile()
            DefaultConn = dbutil.EstablishConnection(DefaultIp, DefaultDbName)
            txtConfig.Text = IPWebservice

            wsThread = New Thread(AddressOf ThreadEventSendDataToHQ)
            wsThread.Priority = ThreadPriority.AboveNormal
            wsThread.IsBackground = True
            wsThread.Start()

            SendSaleSummaryToHeadquarter()
            SendPointSummaryToHeadquarter()
            ExportDocumentToHeadquarter()
            ImportDocumentToBranch()
            SendPayByVoucherToHeadquarter()
            SetTime()
            iTimer.Start()

        Catch ex As Exception
            With NotifyIcon1
                .BalloonTipTitle = "Warning message."
                .BalloonTipText = ex.ToString
                .ShowBalloonTip(1000)
            End With
            WriteErrorLogFile(xErrorForm, ex.ToString, xLogFileName)
        End Try
    End Sub

    '--------------------------------------------------------------
    ' Send Transfer Diliverty.
    '--------------------------------------------------------------

    '-----------------------------------------------------------------------------------------------------
    ' Thread For Send Data To HQ
    '-----------------------------------------------------------------------------------------------------
    Public Sub ThreadEventSendDataToHQ()
        Dim data As Byte() = New Byte(1023) {}
        wsTcpListener = New TcpListener(IPAddress.Any, GetPort.SendDataToHQ)
        wsTcpListener.Start()
        While True
            Try
                wsSocket = wsTcpListener.AcceptSocket()
                If wsSocket.Connected Then
                    wsSocket.Receive(data)
                    Dim cmd As String = System.Text.Encoding.ASCII.GetString(data)
                    If cmd <> "" Then
                        AnalysisServerMsg(wsSocket, cmd)
                    End If
                End If
            Catch err As Exception
                WriteErrorLogFile("Thread", err.ToString, xLogFileName)
            End Try
        End While
    End Sub
    Public Sub AnalysisServerMsg(ByVal sock As Socket, ByVal szMsg As String)
        Dim szSep As Char() = {"|"c}
        Dim szAryMsg As String() = szMsg.Split(szSep)
        Try
            Select Case Integer.Parse(szAryMsg(0))
                Case Is = GetMethod.ExportImportSummarySale
                    If OnprocessFuntion = False Then
                        OnprocessFuntion = True
                        SendSaleSummaryToHeadquarter()
                        OnprocessFuntion = False
                    End If
                Case Is = GetMethod.ExportImportPoint
                    If OnprocessFuntion = False Then
                        OnprocessFuntion = True
                        SendPointSummaryToHeadquarter()
                        OnprocessFuntion = False
                    End If
                Case Is = GetMethod.ExportImportDocument
                    If OnprocessFuntion = False Then
                        OnprocessFuntion = True
                        ImportDocumentToBranch()
                        OnprocessFuntion = False
                    End If
                Case Is = GetMethod.ExportImportCouponVoucher
                    If OnprocessFuntion = False Then
                        OnprocessFuntion = True
                        Dim dt As New DataTable
                        dt = dbutil.List("SELECT * FROM ordertransaction WHERE memberdiscountid >0 AND transactionid=" & Integer.Parse(szAryMsg(1)) & " AND computerid=" & Integer.Parse(szAryMsg(2)), DefaultConn)
                        If dt.Rows.Count > 0 Then
                            SendPointSummaryToHeadquarter()
                            SendPayByVoucherToHeadquarter(Integer.Parse(szAryMsg(1)), Integer.Parse(szAryMsg(2)))
                        End If
                        OnprocessFuntion = False
                    End If
                Case Is = GetMethod.ExportImportAllData
                    If OnprocessFuntion = False Then
                        OnprocessFuntion = True
                        SendSaleSummaryToHeadquarter()
                        ImportDocumentToBranch()
                        SendPayByVoucherToHeadquarter()
                        OnprocessFuntion = False
                    End If
                Case Is = GetMethod.ExportVoucherToHQ

                Case Is = GetMethod.ExportTranferDelivery


            End Select
            OnprocessFuntion = False
        Catch err As Exception
            WriteErrorLogFile("Thread", err.ToString, xLogFileName)
            OnprocessFuntion = False
        End Try
    End Sub
    '-----------------------------------------------------------------------------------------------------
    ' Config Data
    '-----------------------------------------------------------------------------------------------------
    Private Sub SetMainNotify()
        'set ContextMenu
        ctmMain = New ContextMenu
        ctmMain.MenuItems.Add(mniMainProgramSetting)
        ctmMain.MenuItems.Add(mniMainProgramRefresh)
        ctmMain.MenuItems.Add(mniMainProgramExit)

        NotifyIcon1.Visible = True
        mniMainProgramExit.Text = "Exit."
        mniMainProgramRefresh.Text = "Send Data."
        mniMainProgramSetting.Text = "Setting."
        NotifyIcon1.ContextMenu = ctmMain
    End Sub
    Private Sub CreateDatabaseConfigInXMLFile()
        Dim strSection As String
        strSection = DATABASESETTINGNODENAME
        '********** Receipt Font **********************************
        XMLProfile.SetValue(strSection, "IPServer", "127.0.0.1")
        XMLProfile.SetValue(strSection, "DbServer", "promise")
        XMLProfile.SetValue(strSection, "Interval", 30)
        XMLProfile.SetValue(strSection, "URLWebservice", "http://127.0.0.1/webservice/Service.asmx")
        strSection = MANAGEDATASETTINGNODENAME
        '************* Manage Data ******************************
        XMLProfile.SetValue(strSection, "ExchangeDirectory", "C:\pRoMiSeData\")
        XMLProfile.SetValue(strSection, "MySQLDumpPath", "C:\mysql5\bin\")
        XMLProfile.SetValue(strSection, "RegionID", 0)
    End Sub
    Private Sub LoadDatabaseSettingConfigFromXMLFile()
        Dim strSection As String
        strSection = DATABASESETTINGNODENAME
        DefaultIp = XMLProfile.GetValue(strSection, "IPAddress")
        DefaultDbName = XMLProfile.GetValue(strSection, "DBName")
        iTime = XMLProfile.GetValue(strSection, "Interval")
        IPWebservice = XMLProfile.GetValue(strSection, "URLWebservice")
    End Sub
    Private Sub LoadManageDataSettingConfigFromXMLFile()
        Dim strSection As String
        strSection = MANAGEDATASETTINGNODENAME
        'Manage Data
        ManageDataExchangeFolder = XMLProfile.GetValue(strSection, "ExchangeDirectory")
        MySQLDumpPath = XMLProfile.GetValue(strSection, "MySQLDumpPath")
        RegionID = XMLProfile.GetValue(strSection, "RegionID")
    End Sub
    Private Sub RefreshAllRetailFrontXMLData()
        'Load Database
        If XMLProfile.HasSection(DATABASESETTINGNODENAME) = False Then
            CreateDatabaseConfigInXMLFile()
        Else
            LoadDatabaseSettingConfigFromXMLFile()
        End If
    End Sub
    Private Sub SetTime()
        Select Case Now.Minute
            Case 1 To 9
                TimeSet = New DateTime(Now.Year, Now.Month, Now.Day, Now.Hour, 10, 0)
            Case 11 To 19
                TimeSet = New DateTime(Now.Year, Now.Month, Now.Day, Now.Hour, 20, 0)
            Case 21 To 29
                TimeSet = New DateTime(Now.Year, Now.Month, Now.Day, Now.Hour, 30, 0)
            Case 31 To 39
                TimeSet = New DateTime(Now.Year, Now.Month, Now.Day, Now.Hour, 40, 0)
            Case 41 To 49
                TimeSet = New DateTime(Now.Year, Now.Month, Now.Day, Now.Hour, 50, 0)
            Case 51 To 59
                TimeSet = New DateTime(Now.Year, Now.Month, Now.Day, Now.Hour, 59, 0)
                TimeSet = TimeSet.AddMinutes(1)

            Case Else
                TimeSet = Date.Now
        End Select
    End Sub
    '-----------------------------------------------------------------------------------------------------
    ' Manage Data
    '-----------------------------------------------------------------------------------------------------
    Private Sub ImportDocumentToBranch()
        Dim msResultText As String = ""
        Try
            Dim xPath As String = ManageDataExchangeFolder & "/" & FOLDER_ERROR
            If Not System.IO.Directory.Exists(xPath) Then
                System.IO.Directory.CreateDirectory(xPath)
            End If

            Dim IPHQ As String = ""
            Dim ResultText As String = ""
            Dim dsResult() As DataSet
            ReDim dsResult(-1)

            IPHQ = SeachIPConnection()
            If PingIPAddress(IPHQ) = True Then
                Dim ManageData As New pRoMiSe_ManageData_Class.pRoMiSeExportImportDataProcess(DefaultIp, DefaultDbName, RegionID, 1, ManageDataExchangeFolder, Application.StartupPath, pRoMiSe_ManageData_Class.ProgramFor.Branch)

                '------------------------------------------------------------------------------
                ' Get Document At Headquarter
                '------------------------------------------------------------------------------
                Dim msgResult As String = ""
                msgResult += "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                If ExportDataSetToBranch(dsResult, RegionID, ResultText) = False Then
                    msgResult += "" & ResultText & vbCrLf
                    msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                    WriteErrorLogFile("AutoExportDocumentAtHQ", msgResult, xLogFileName)
                Else
                    msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                    msgResult += "Successfully."
                    WriteErrorLogFile("AutoExportDocumentAtHQ", msgResult, xLogFileName)
                    For i As Integer = 0 To dsResult.Length - 1
                        Dim dsResultCopy As New DataSet
                        dsResultCopy = dsResult(i).Copy()
                        msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                        If ManageData.AutoImportDataSetToBranch(DefaultConn, dsResultCopy, ResultText) = False Then
                            WriteErrorLogFile("AutoImportDocumentAtBranch", ResultText, xLogFileName)
                            With NotifyIcon1
                                .BalloonTipTitle = "Warning message."
                                .BalloonTipText = ResultText
                                .ShowBalloonTip(1000)
                            End With
                            msResultText = "AutoImportDocumentDataSetToBranch"
                            msgResult += "" & ResultText & vbCrLf
                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                            WriteErrorLogFile("AutoImportDocumentDataSetToBranch", msgResult, xLogFileName)

                            msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            msgResult += "Fail." & vbCrLf
                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                            WriteErrorLogFile("AutoSetDocumentDataInDataSetExportTotBranchAtHQ", msgResult, xLogFileName)
                        Else

                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            msgResult += "Successfully."
                            WriteErrorLogFile("AutoImportDocumentDataSetToBranch", msgResult, xLogFileName)
                            msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            If AutoUpdateDataSetToHQ(dsResultCopy, ResultText) = False Then
                                With NotifyIcon1
                                    .BalloonTipTitle = "Warning message."
                                    .BalloonTipText = ResultText
                                    .ShowBalloonTip(1000)
                                End With
                                msResultText = "AutoSetDocumentDataInDataSetExportTotBranchAtHQ"
                                msgResult += "" & ResultText & vbCrLf
                                msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                                WriteErrorLogFile("AutoSetDocumentDataInDataSetExportTotBranchAtHQ", msgResult, xLogFileName)
                            Else
                                With NotifyIcon1
                                    .BalloonTipTitle = "Warning message."
                                    .BalloonTipText = "Send completed."
                                    .ShowBalloonTip(1000)
                                End With
                                msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                                msgResult += "Successfully."
                                WriteErrorLogFile("AutoSetDocumentDataInDataSetExportTotBranchAtHQ", msgResult, xLogFileName)
                            End If
                        End If
                    Next
                End If

            End If
        Catch ex As Exception
            WriteErrorLogFile(msResultText, ex.ToString, xLogFileName)
        End Try
    End Sub
    Private Sub SendSaleSummaryToHeadquarter()
        Dim msResultText As String = ""

        Try
            'Dim xPath As String = Application.StartupPath & "/" & FOLDER_ERROR
            Dim xPath As String = ManageDataExchangeFolder & "/" & FOLDER_ERROR
            If Not System.IO.Directory.Exists(xPath) Then
                System.IO.Directory.CreateDirectory(xPath)
            End If
            '------------------------------------------------------------------------------
            ' Ping IP Headquarter
            '------------------------------------------------------------------------------
            Dim IPHQ As String = ""
            IPHQ = SeachIPConnection()
            If PingIPAddress(IPHQ) = True Then
                Dim ManageData As New pRoMiSe_ManageData_Class.pRoMiSeExportImportDataProcess(DefaultIp, DefaultDbName, RegionID, 1, ManageDataExchangeFolder, Application.StartupPath, pRoMiSe_ManageData_Class.ProgramFor.HeadQuarter)
                Dim DsSaleReport() As DataSet
                ReDim DsSaleReport(-1)
                Dim DsUpdateTransfer As DataSet
                Dim xResultText As String = ""

                Dim msgResult As String = ""
                msgResult += "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                If ManageData.AutoExportDataSetToHQ(DefaultConn, RegionID, DsSaleReport, xResultText) = False Then
                    'ExportData äÁè¼èÒ¹
                    msResultText = "AutoExportDataSetToHQ"
                    With NotifyIcon1
                        .BalloonTipTitle = "Warning message."
                        .BalloonTipText = xResultText
                        .ShowBalloonTip(1000)
                    End With
                    msgResult += "" & xResultText.ToString & vbCrLf
                    msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                    WriteErrorLogFile("AutoExportDataSetToHQ", msgResult.ToString, xLogFileName)
                    Exit Sub
                Else
                    msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                    msgResult += "Successfully."
                    WriteErrorLogFile("AutoExportDataSetToHQ", msgResult.ToString, xLogFileName)

                    For i As Integer = 0 To DsSaleReport.Length - 1
                        DsUpdateTransfer = DsSaleReport(i).Copy
                        msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                        If ImportSummarySaleByDateToHQ(DsSaleReport(i), xResultText) = False Then
                            With NotifyIcon1
                                .BalloonTipTitle = "Warning message."
                                .BalloonTipText = xResultText
                                .ShowBalloonTip(1000)
                            End With
                            msResultText = "ImportSummarySaleByDateToHQ"
                            msgResult += "" & xResultText.ToString & vbCrLf
                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                            WriteErrorLogFile("ImportSummarySaleByDateToHQ", msgResult.ToString, xLogFileName)

                            msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            msgResult += "Fail." & vbCrLf
                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                            WriteErrorLogFile("AutoSetDataInDataSetExportToHQAtBranch", msgResult, xLogFileName)

                        Else
                            'Update Transection

                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            msgResult += "Successfully."
                            WriteErrorLogFile("ImportSummarySaleByDateToHQ", msgResult.ToString, xLogFileName)
                            msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            If ManageData.AutoSetDataInDataSetExportToHQAtBranch(DefaultConn, DsUpdateTransfer, xResultText) = False Then
                                With NotifyIcon1
                                    .BalloonTipTitle = "Warning message."
                                    .BalloonTipText = xResultText
                                    .ShowBalloonTip(1000)
                                End With
                                msgResult += "" & xResultText.ToString & vbCrLf
                                msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                                WriteErrorLogFile("AutoSetDataInDataSetExportToHQAtBranch", msgResult.ToString, xLogFileName)
                            Else

                                msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                                msgResult += "Successfully."
                                WriteErrorLogFile("AutoSetDataInDataSetExportToHQAtBranch", msgResult.ToString, xLogFileName)
                            End If
                        End If
                    Next

                End If
            Else
                With NotifyIcon1
                    .BalloonTipTitle = "Warning message."
                    .BalloonTipText = "Unable to connect to the headquarters"
                    .ShowBalloonTip(1000)
                End With
                msResultText = "ImportSummarySaleByDateToHQ"
                WriteErrorLogFile(msResultText, "Can't connect IP Address " & IPHQ, xLogFileName)
            End If
        Catch ex As Exception
            With NotifyIcon1
                .BalloonTipTitle = "Warning message."
                .BalloonTipText = ex.ToString
                .ShowBalloonTip(1000)
            End With
            '
            msResultText = "ImportSummarySaleByDateToHQ"
            WriteErrorLogFile(msResultText, ex.ToString, xLogFileName)
        End Try
    End Sub
    Private Sub SendPointSummaryToHeadquarter()
        Dim msResultText As String = ""
        Try
            'Dim xPath As String = Application.StartupPath & "/" & FOLDER_ERROR
            Dim xPath As String = ManageDataExchangeFolder & "/" & FOLDER_ERROR
            If Not System.IO.Directory.Exists(xPath) Then
                System.IO.Directory.CreateDirectory(xPath)
            End If
            '------------------------------------------------------------------------------
            ' Ping IP Headquarter
            '------------------------------------------------------------------------------
            Dim IPHQ As String = ""
            IPHQ = SeachIPConnection()
            If PingIPAddress(IPHQ) = True Then
                Dim ManageData As New pRoMiSe_ManageData_Class.pRoMiSeExportImportDataProcess(DefaultIp, DefaultDbName, RegionID, 1, ManageDataExchangeFolder, Application.StartupPath, pRoMiSe_ManageData_Class.ProgramFor.HeadQuarter)
                Dim DsPointSummary() As DataSet
                ReDim DsPointSummary(-1)
                Dim DsUpdateDsPointSummary As DataSet
                Dim xResultText As String = ""

                Dim msgResult As String = ""
                msgResult += "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                If ManageData.AutoExportRedeemRewardPointDataToHQ(DefaultConn, RegionID, DsPointSummary, xResultText) = False Then
                    With NotifyIcon1
                        .BalloonTipTitle = "Warning message."
                        .BalloonTipText = xResultText
                        .ShowBalloonTip(1000)
                    End With
                    msResultText = "AutoExportRedeemRewardPointDataToHQ"
                    msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                    msgResult += "" & xResultText
                    WriteErrorLogFile("AutoExportRedeemRewardPointDataToHQ", msgResult, xLogFileName)
                    Exit Sub
                Else
                    msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                    msgResult += "Successfully."
                    WriteErrorLogFile("AutoExportRedeemRewardPointDataToHQ", msgResult, xLogFileName)
                    For i As Integer = 0 To DsPointSummary.Length - 1
                        DsUpdateDsPointSummary = DsPointSummary(i).Copy
                        msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                        If ImportSummarySaleByDateToHQ(DsPointSummary(i), xResultText) = False Then
                            With NotifyIcon1
                                .BalloonTipTitle = "Warning message."
                                .BalloonTipText = xResultText
                                .ShowBalloonTip(1000)
                            End With
                            msResultText = "ImportRedeemRewardToHQ"
                            msgResult += "" & xResultText & vbCrLf
                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                            WriteErrorLogFile("ImportRedeemRewardToHQ", msgResult, xLogFileName)

                            msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            msgResult += "Fail." & vbCrLf
                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                            WriteErrorLogFile("AutoSetRedeemRewardDataInDataSetExportToHQAtBranch", msgResult, xLogFileName)
                        Else
                            'Update Transection
                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            msgResult += "Successfully."
                            WriteErrorLogFile("ImportRedeemRewardToHQ", msgResult, xLogFileName)
                            msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            If ManageData.AutoSetDataInDataSetExportToHQAtBranch(DefaultConn, DsUpdateDsPointSummary, xResultText) = False Then
                                With NotifyIcon1
                                    .BalloonTipTitle = "Warning message."
                                    .BalloonTipText = xResultText
                                    .ShowBalloonTip(1000)
                                End With
                                msResultText = "AutoSetRedeemRewardDataInDataSetExportToHQAtBranch"
                                msgResult += "" & xResultText & vbCrLf
                                msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                                WriteErrorLogFile("AutoSetRedeemRewardDataInDataSetExportToHQAtBranch", msgResult, xLogFileName)
                            Else
                                msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                                msgResult += "Successfully."
                                WriteErrorLogFile("AutoSetRedeemRewardDataInDataSetExportToHQAtBranch", msgResult, xLogFileName)
                            End If

                        End If
                    Next
                End If
            Else
                With NotifyIcon1
                    .BalloonTipTitle = "Warning message."
                    .BalloonTipText = "Unable to connect to the headquarters"
                    .ShowBalloonTip(1000)
                End With
                msResultText = "SendPointSummaryToHeadquarter"
                WriteErrorLogFile(msResultText, "Can't connect IP Address " & IPHQ, xLogFileName)
            End If
        Catch ex As Exception
            With NotifyIcon1
                .BalloonTipTitle = "Warning message."
                .BalloonTipText = ex.ToString
                .ShowBalloonTip(1000)
            End With
            msResultText = "SendPointSummaryToHeadquarter"
            WriteErrorLogFile(msResultText, ex.ToString, xLogFileName)
        End Try
    End Sub
    Private Sub ExportDocumentToHeadquarter()
        Dim msResultText As String = ""
        Try
            'Dim xPath As String = Application.StartupPath & "/" & FOLDER_ERROR
            Dim xPath As String = ManageDataExchangeFolder & "/" & FOLDER_ERROR
            If Not System.IO.Directory.Exists(xPath) Then
                System.IO.Directory.CreateDirectory(xPath)
            End If
            '------------------------------------------------------------------------------
            ' Ping IP Headquarter
            '------------------------------------------------------------------------------
            Dim IPHQ As String = ""
            IPHQ = SeachIPConnection()
            If PingIPAddress(IPHQ) = True Then
                Dim ManageData As New pRoMiSe_ManageData_Class.pRoMiSeExportImportDataProcess(DefaultIp, DefaultDbName, RegionID, 1, ManageDataExchangeFolder, Application.StartupPath, pRoMiSe_ManageData_Class.ProgramFor.HeadQuarter)
                Dim DsPointSummary() As DataSet
                ReDim DsPointSummary(-1)
                Dim DsUpdateDsPointSummary As DataSet
                Dim xResultText As String = ""

                Dim msgResult As String = ""
                msgResult += "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                If ManageData.AutoExportDocumentDataToHQ(DefaultConn, RegionID, DsPointSummary, xResultText) = False Then
                    With NotifyIcon1
                        .BalloonTipTitle = "Warning message."
                        .BalloonTipText = xResultText
                        .ShowBalloonTip(1000)
                    End With
                    msResultText = "AutoExportDocumentDataToHQAtBranch"
                    msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                    msgResult += "" & xResultText
                    WriteErrorLogFile("AutoExportDocumentDataToHQAtBranch", msgResult, xLogFileName)
                    Exit Sub
                Else
                    msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                    msgResult += "Successfully."
                    WriteErrorLogFile("AutoExportDocumentDataToHQAtBranch", msgResult, xLogFileName)
                    For i As Integer = 0 To DsPointSummary.Length - 1
                        DsUpdateDsPointSummary = DsPointSummary(i).Copy
                        msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                        If ImportSummarySaleByDateToHQ(DsPointSummary(i), xResultText) = False Then
                            With NotifyIcon1
                                .BalloonTipTitle = "Warning message."
                                .BalloonTipText = xResultText
                                .ShowBalloonTip(1000)
                            End With
                            msResultText = "ImportDocumentToHQ"
                            msgResult += "" & xResultText & vbCrLf
                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                            WriteErrorLogFile("ImportDocumentToHQ", msgResult, xLogFileName)

                            msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            msgResult += "Fail." & vbCrLf
                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                            WriteErrorLogFile("AutoSetDocumentDataInDataSetExportToHQAtBranch", msgResult, xLogFileName)
                        Else
                            'Update Transection
                            msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            msgResult += "Successfully."
                            WriteErrorLogFile("ImportDocumentToHQ", msgResult, xLogFileName)
                            msgResult = "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                            If ManageData.AutoSetDataInDataSetExportToHQAtBranch(DefaultConn, DsUpdateDsPointSummary, xResultText) = False Then
                                With NotifyIcon1
                                    .BalloonTipTitle = "Warning message."
                                    .BalloonTipText = xResultText
                                    .ShowBalloonTip(1000)
                                End With
                                msResultText = "AutoSetDocumentDataInDataSetExportToHQAtBranch"
                                msgResult += "" & xResultText & vbCrLf
                                msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                                WriteErrorLogFile("AutoSetDocumentDataInDataSetExportToHQAtBranch", msgResult, xLogFileName)
                            Else
                                msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                                msgResult += "Successfully."
                                WriteErrorLogFile("AutoSetDocumentDataInDataSetExportToHQAtBranch", msgResult, xLogFileName)
                            End If

                        End If
                    Next
                End If
            Else
                With NotifyIcon1
                    .BalloonTipTitle = "Warning message."
                    .BalloonTipText = "Unable to connect to the headquarters"
                    .ShowBalloonTip(1000)
                End With
                msResultText = "ExportDocumentToHeadquarter"
                WriteErrorLogFile(msResultText, "Can't connect IP Address " & IPHQ, xLogFileName)
            End If
        Catch ex As Exception
            With NotifyIcon1
                .BalloonTipTitle = "Warning message."
                .BalloonTipText = ex.ToString
                .ShowBalloonTip(1000)
            End With
            msResultText = "ExportDocumentToHeadquarter"
            WriteErrorLogFile(msResultText, ex.ToString, xLogFileName)
        End Try
    End Sub
    'Protected Overrides Sub WndProc(ByRef m As Message)
    '    MyBase.WndProc(m)
    '    Select Case m.Msg
    '        Case Is = 20001
    '            SendPointSummaryToHeadquarter()
    '        Case Is = 20002
    '            SendSaleSummaryToHeadquarter()
    '        Case Is = 20003
    '            Me.Close()
    '        Case Is = 20004
    '            ImportDocumentToBranch()
    '        Case Is = 20005
    '            ImportDocumentToBranch()
    '    End Select

    'End Sub
    Private Overloads Sub SendPayByVoucherToHeadquarter(ByVal transactionId As Integer, ByVal computerId As Integer)
        Dim msResultText As String = ""
        Try
            'Dim xPath As String = Application.StartupPath & "/" & FOLDER_ERROR
            Dim xPath As String = ManageDataExchangeFolder & "/" & FOLDER_ERROR
            If Not System.IO.Directory.Exists(xPath) Then
                System.IO.Directory.CreateDirectory(xPath)
            End If
            '------------------------------------------------------------------------------
            ' Ping IP Headquarter
            '------------------------------------------------------------------------------
            Dim IPHQ As String = ""
            Dim DtPayByVoucher As New DataTable
            Dim DsPayment As New DataSet
            Dim StrBD As New StringBuilder
            Dim xResultText As String = ""
            IPHQ = SeachIPConnection()
            DtPayByVoucher = GetPayment_Paybyvoucher(transactionId, computerId)
            DtPayByVoucher.TableName = "DtPayByVoucher"
            If DtPayByVoucher.Rows.Count > 0 Then
                If PingIPAddress(IPHQ) = True Then
                    DsPayment.Tables.Add(DtPayByVoucher)
                    Dim msgResult As String = ""
                    msgResult += "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                    If Payment_PaybyvoucherSendToHQ(DsPayment, xResultText) = True Then
                        For i As Integer = 0 To DtPayByVoucher.Rows.Count - 1
                            StrBD.Append("UPDATE paybyvoucher SET alreadyexporttohq=1 WHERE  transactionid=" & DtPayByVoucher.Rows(i)("transactionid") & " AND computerid=" & DtPayByVoucher.Rows(i)("computerid") & ";")
                        Next
                        dbutil.sqlExecute(StrBD.ToString, DefaultConn)
                        msResultText = "SendPayByVoucherToHQ"
                        msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                        msgResult += "Successfully."
                        WriteErrorLogFile(msResultText, msgResult, xLogFileName)
                    Else
                        With NotifyIcon1
                            .BalloonTipTitle = "Warning message."
                            .BalloonTipText = xResultText
                            .ShowBalloonTip(1000)
                        End With
                        msResultText = "SendPayByVoucherToHQ"
                        msgResult += "Fail. :" & xResultText & vbCrLf
                        msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                        WriteErrorLogFile(msResultText, msgResult, xLogFileName)
                    End If
                Else
                    With NotifyIcon1
                        .BalloonTipTitle = "Warning message."
                        .BalloonTipText = "Unable to connect to the headquarters"
                        .ShowBalloonTip(1000)
                    End With
                    msResultText = "SendPayByVoucherToHQ"
                    WriteErrorLogFile(msResultText, "Can't connect IP Address " & IPHQ, xLogFileName)
                End If
            End If
        Catch ex As Exception
            With NotifyIcon1
                .BalloonTipTitle = "Warning message."
                .BalloonTipText = ex.ToString
                .ShowBalloonTip(1000)
            End With
            msResultText = "SendPayByVoucherToHQ"
            WriteErrorLogFile(msResultText, ex.ToString, xLogFileName)
        End Try
    End Sub
    Private Overloads Sub SendPayByVoucherToHeadquarter()
        Dim msResultText As String = ""
        Try
            'Dim xPath As String = Application.StartupPath & "/" & FOLDER_ERROR
            Dim xPath As String = ManageDataExchangeFolder & "/" & FOLDER_ERROR
            If Not System.IO.Directory.Exists(xPath) Then
                System.IO.Directory.CreateDirectory(xPath)
            End If
            '------------------------------------------------------------------------------
            ' Ping IP Headquarter
            '------------------------------------------------------------------------------
            Dim IPHQ As String = ""
            Dim DtPayByVoucher As New DataTable
            Dim DsPayment As New DataSet
            Dim StrBD As New StringBuilder
            Dim xResultText As String = ""
            IPHQ = SeachIPConnection()
            DtPayByVoucher = GetPayment_Paybyvoucher()
            DtPayByVoucher.TableName = "DtPayByVoucher"
            If DtPayByVoucher.Rows.Count > 0 Then
                If PingIPAddress(IPHQ) = True Then
                    DsPayment.Tables.Add(DtPayByVoucher)
                    Dim msgResult As String = ""
                    msgResult += "StartTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                    If Payment_PaybyvoucherSendToHQ(DsPayment, xResultText) = True Then
                        For i As Integer = 0 To DtPayByVoucher.Rows.Count - 1
                            StrBD.Append("UPDATE paybyvoucher SET alreadyexporttohq=1 WHERE  transactionid=" & DtPayByVoucher.Rows(i)("transactionid") & " AND computerid=" & DtPayByVoucher.Rows(i)("computerid") & ";")
                        Next
                        dbutil.sqlExecute(StrBD.ToString, DefaultConn)
                        msResultText = "SendPayByVoucherToHQ"
                        msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss") & vbCrLf
                        msgResult += "Successfully."
                        WriteErrorLogFile(msResultText, msgResult, xLogFileName)
                    Else
                        With NotifyIcon1
                            .BalloonTipTitle = "Warning message."
                            .BalloonTipText = xResultText
                            .ShowBalloonTip(1000)
                        End With
                        msResultText = "SendPayByVoucherToHQ"
                        msgResult += "Fail. :" & xResultText & vbCrLf
                        msgResult += "EndTime:: " & Format(Now, "yyyy-MM-dd hh:mm:ss")
                        WriteErrorLogFile(msResultText, msgResult, xLogFileName)
                    End If
                Else
                    With NotifyIcon1
                        .BalloonTipTitle = "Warning message."
                        .BalloonTipText = "Unable to connect to the headquarters"
                        .ShowBalloonTip(1000)
                    End With
                    msResultText = "SendPayByVoucherToHQ"
                    WriteErrorLogFile(msResultText, "Can't connect IP Address " & IPHQ, xLogFileName)
                End If
            End If
        Catch ex As Exception
            With NotifyIcon1
                .BalloonTipTitle = "Warning message."
                .BalloonTipText = ex.ToString
                .ShowBalloonTip(1000)
            End With
            msResultText = "SendPayByVoucherToHQ"
            WriteErrorLogFile(msResultText, ex.ToString, xLogFileName)
        End Try
    End Sub
    '-----------------------------------------------------------------------------------------------------
    ' Manage Form
    '-----------------------------------------------------------------------------------------------------
    Private Sub Main_Resize(ByVal sender As Object, ByVal e As System.EventArgs) Handles Me.Resize
        If (Me.WindowState = FormWindowState.Minimized) Then
            Me.Hide()
            NotifyIcon1.Visible = True
        Else
            'Me.Focus()
        End If
    End Sub
    Private Sub mniMainProgramExit_Click(ByVal sender As Object, ByVal e As System.EventArgs) Handles mniMainProgramExit.Click
        Me.Close()
    End Sub
    Private Sub mniMainProgramSetting_Click(ByVal sender As Object, ByVal e As System.EventArgs) Handles mniMainProgramSetting.Click
        Me.Show()
    End Sub
    Private Sub mniMainProgramRefresh_Click(ByVal sender As Object, ByVal e As System.EventArgs) Handles mniMainProgramRefresh.Click
        'Stop timer
        iTimer.Stop()
        mniMainProgramRefresh.Enabled = False
        SendSaleSummaryToHeadquarter()
        SendPointSummaryToHeadquarter()
        ExportDocumentToHeadquarter()
        ImportDocumentToBranch()
        SendPayByVoucherToHeadquarter()
        TimeSet = TimeSet.AddMinutes(iTime)
        mniMainProgramRefresh.Enabled = True
        OnprocessFuntion = False
        iTimer.Start()
    End Sub
    '-----------------------------------------------------------------------------------------------------
    ' Manage Form
    '-----------------------------------------------------------------------------------------------------
    Private Function WriteErrorLogFile(ByVal errorFrom As String, ByVal errorString As String, _
        ByVal logFileName As String) As Boolean
        Dim strErrorLogFileName As String
        strErrorLogFileName = ManageDataExchangeFolder & FOLDER_ERROR & System.IO.Path.DirectorySeparatorChar & _
                                logFileName & "_" & Format(Now, "yyyyMMdd")
        strErrorLogFileName &= ".txt"
        Try
            'File stream and text stream
            Dim fsWrite As System.IO.FileStream = New System.IO.FileStream(strErrorLogFileName, IO.FileMode.Append, _
                                    IO.FileAccess.Write, IO.FileShare.Write)
            Dim wr As System.IO.StreamWriter = New System.IO.StreamWriter(fsWrite)
            wr.WriteLine("Message At " & errorFrom)
            wr.WriteLine(errorString)
            wr.WriteLine("--------------------------------------------------------------------------------")
            wr.Close()
            fsWrite.Close()
        Catch e As Exception
            Return False
        End Try
        Return True
    End Function
    Private Function PingIPAddress(ByVal szIPAddress As String) As Boolean
        Try
            Dim ping As New Ping()
            Dim reply As PingReply = ping.Send(szIPAddress, 3000)
            ' 3 sec 
            If reply.Status <> IPStatus.Success Then
                ' Server cannot connect 
                Return False
            Else
                Return True
            End If
        Catch e As Exception
            Return False
        End Try
    End Function
    Private Sub iTimer_Tick(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles iTimer.Tick
        If TimeSet < Now Then
            iTimer.Stop()
            If OnprocessFuntion = False Then
                OnprocessFuntion = True
                mniMainProgramRefresh.Enabled = False
                SendSaleSummaryToHeadquarter()
                SendPointSummaryToHeadquarter()
                ExportDocumentToHeadquarter()
                ImportDocumentToBranch()
                SendPayByVoucherToHeadquarter()
                iTimer.Interval = iTime * 1000 * 60
                mniMainProgramRefresh.Enabled = True
            End If
            OnprocessFuntion = False
            iTimer.Start()
        End If
    End Sub
    Private Sub Close_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Quit.Click
        Me.Hide()
    End Sub
    Private Sub Save_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Save.Click
        Dim strSection As String
        strSection = DATABASESETTINGNODENAME
        Try
            XMLProfile.SetValue(strSection, "URLWebservice", txtConfig.Text)
            MessageBox.Show("Successfully", "Warning message.", MessageBoxButtons.OK, MessageBoxIcon.Information)
        Catch ex As Exception
            MessageBox.Show("An error has occurred.", "Warning message.", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Try
    End Sub
    Private Sub Button1_Click(ByVal sender As System.Object, ByVal e As System.EventArgs) Handles Button1.Click
        Try
            Dim strResultText As String = ""

            If ClspRoMiSeForWeb.CallWebservice.SendPointSummaryToHeadquarter(DefaultConn, strResultText) = False Then
                MessageBox.Show("An error has occurred.", "Warning message.", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Else
                MessageBox.Show("Send completed.", "Warning message.", MessageBoxButtons.OK, MessageBoxIcon.Information)
            End If
            DefaultConn.Close()
        Catch ex As Exception
            DefaultConn.Close()
        End Try
    End Sub
    '-----------------------------------------------------------------------------------------------------
    ' Get Data
    '-----------------------------------------------------------------------------------------------------
    Private Function SeachIPConnection() As String
        Dim sql As String
        Dim Dt As New DataTable
        sql = "select * from productregion where productlevelid=1"
        Dt = dbutil.List(sql, DefaultConn)
        Return Dt.Rows(0)("IP_Connection")
    End Function
    Private Function SeachIPHQ() As String
        Dim sql As String
        Dim Dt As New DataTable
        sql = "select * from productlevel where productlevelid=1"
        Dt = dbutil.List(sql, DefaultConn)
        Return Dt.Rows(0)("IPAddress")
    End Function
    Private Function SettingRegionID(ByVal IPConfig As String) As Integer
        Dim sql As String
        sql = "update productlevel set IPAddress='" & IPConfig & "' Where productlevelid=1"
        dbutil.sqlExecute(sql, DefaultConn)
    End Function
    Private Function GetMemberData() As DataTable
        Dim strSQL As String
        Dim dt As New DataTable
        strSQL = "select * from members where alreadyexporttohq=0"
        dt = dbutil.List(strSQL, DefaultConn)
        Return dt
    End Function
    Private Overloads Function GetPayment_Paybyvoucher(ByVal transactionId As Integer, ByVal computerId As Integer) As DataTable
        Dim StrSQL As String
        Dim DtData As New DataTable
        StrSQL = "SELECT * FROM paybyvoucher WHERE transactionid=" & transactionId & " and  computerid=" & computerId
        DtData = dbutil.List(StrSQL, DefaultConn)
        Return DtData
    End Function
    Private Overloads Function GetPayment_Paybyvoucher() As DataTable
        Dim StrSQL As String
        Dim DtData As New DataTable
        StrSQL = "SELECT * FROM paybyvoucher WHERE alreadyexporttohq=0 LIMIT 300"
        DtData = dbutil.List(StrSQL, DefaultConn)
        Return DtData
    End Function
End Class
