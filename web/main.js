const { app, BrowserWindow, protocol, net } = require('electron')
const path = require('path')
const fs = require('fs')
const { pathToFileURL } = require('url')

// app:// şemasını standart bir origin gibi davranacak şekilde kaydet
protocol.registerSchemesAsPrivileged([
  { scheme: 'app', privileges: { standard: true, secure: true, supportFetchAPI: true, corsEnabled: true } },
])

app.commandLine.appendSwitch('high-dpi-support', '1')
app.commandLine.appendSwitch('force-device-scale-factor', '1')
app.commandLine.appendSwitch('ignore-certificate-errors')

function createWindow() {
  const mainWindow = new BrowserWindow({
    width: 1440,
    height: 900,
    minWidth: 1024,
    minHeight: 600,
    title: 'API Insight Studio',
    backgroundColor: '#030712',
    show: false,
    webPreferences: {
      nodeIntegration: false,
      contextIsolation: true,
      zoomFactor: 0.9,
    },
  })

  mainWindow.loadURL('app://localhost/')

  mainWindow.once('ready-to-show', () => {
    mainWindow.show()
  })
}

app.whenReady().then(() => {
  const outDir = path.join(__dirname, 'out')

  protocol.handle('app', (request) => {
    const { pathname } = new URL(request.url)

    // Baştaki / karakterini kaldır
    let filePath = pathname.startsWith('/') ? pathname.slice(1) : pathname

    // Kök istek → index.html
    if (!filePath) filePath = 'index.html'

    let fullPath = path.join(outDir, filePath)

    // Uzantısız yol: dizin/index.html veya .html dene
    const cleanPath = filePath.replace(/\/$/, '')
    if (!path.extname(cleanPath)) {
      const withIndex = path.join(outDir, filePath, 'index.html')
      const withHtml  = path.join(outDir, cleanPath + '.html')
      if (fs.existsSync(withIndex)) {
        fullPath = withIndex
      } else if (fs.existsSync(withHtml)) {
        fullPath = withHtml
      } else {
        fullPath = path.join(outDir, 'index.html')
      }
    }

    return net.fetch(pathToFileURL(fullPath).toString())
  })

  createWindow()

  app.on('activate', () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow()
  })
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') app.quit()
})
