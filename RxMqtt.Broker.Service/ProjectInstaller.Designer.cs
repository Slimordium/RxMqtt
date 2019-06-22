namespace RxMqtt.Broker.Service
{
    partial class ProjectInstaller
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.RxMqttBrokerProcessinstaller = new System.ServiceProcess.ServiceProcessInstaller();
            this.RxMqttBrokerInstaller = new System.ServiceProcess.ServiceInstaller();
            // 
            // RxMqttBrokerProcessinstaller
            // 
            this.RxMqttBrokerProcessinstaller.Account = System.ServiceProcess.ServiceAccount.NetworkService;
            this.RxMqttBrokerProcessinstaller.Password = null;
            this.RxMqttBrokerProcessinstaller.Username = null;
            // 
            // RxMqttBrokerInstaller
            // 
            this.RxMqttBrokerInstaller.DisplayName = "RxMqtt.Broker";
            this.RxMqttBrokerInstaller.ServiceName = "RxMqtt.Broker";
            // 
            // ProjectInstaller
            // 
            this.Installers.AddRange(new System.Configuration.Install.Installer[] {
            this.RxMqttBrokerProcessinstaller,
            this.RxMqttBrokerInstaller});

        }

        #endregion

        private System.ServiceProcess.ServiceProcessInstaller RxMqttBrokerProcessinstaller;
        private System.ServiceProcess.ServiceInstaller RxMqttBrokerInstaller;
    }
}