
![favicon](https://github.com/user-attachments/assets/65727c13-a402-45c4-907c-3bc3ef7bbca7)

# Solis Manager - Automated Battery Management for Solis Inverters and Octopus Agile

This app is designed to optimally manage the battery charging for your Solar/PV/Battery system, when
used with the Octopus Agile tariff. The idea is that it will analyse upcoming Agile prices, and then
apply an opinionated strategy to manage your battery based on the cheapest periods.

### Screenshots:

<img width="1118" alt="SolisManagerView" src="https://github.com/user-attachments/assets/1f39bda4-35ff-4607-8437-1589e025d3ce" />

<img width="1139" alt="ChartView" src="https://github.com/user-attachments/assets/41b313aa-0908-4ea8-9191-76946acdb54a" />

### Mobile: 

<img src="https://github.com/user-attachments/assets/4ab0001e-1fa9-42d9-a5b1-c94e4cddcf8a" width=400 height=auto/>
&nbsp;
<img src="https://github.com/user-attachments/assets/c6a36fe7-3f7d-451e-b339-8aed458f3e54" width=400 height=auto/>

### But why not just use the excellent [PredBat](https://springfall2008.github.io/batpred/) plugin for Home Assistant?

I spent quite a lot of time researching PredBat. It looks awesome, and I would love to run it. However, Solis support
for Predbat is quite limited, which makes it unsuitable for my needs. Specifically, there is no current way to run 
PredBat with a Solis Inverter, solely using the Solis API. This means that there are only two alternatives:

* Run the Solis Inverter and PredBat / Home Assistant using Modbus. The problem with that is that the Solis Wifi dongle
  cannot support Modbus _and_ the Solis API, at the same time. So running Predbat with Modbus means losing the SolisCloud
  application which is excellent for monitoring the inverter state.
* The only way to get ModBus working **and** continue to use SolisCloud, is to use custom hardware, and that's not a road
  I'm interested in going down.

As soon as somebody writes an add-in / configuration for Predbat that supports Solis via the SolisCloud API, and which also
implements API control conflation to reduce EEPROM writes on the inverter (see below), then I will probably switch to using 
PredBat, as it's a far superior product. At that point, I'll likely archive this project.

### Warranty / Disclaimer 

**PLEASE NOTE:** This application is provided as-is, with no guarantees; I accept no liability for any issues that arise with 
your electricity bill, inverter, battery, or any other system, as a result of using this application. **Your
choice to run the app is entirely at your own risk.**

### Security Warning

 **Warning**: This application should **never** be exposed to the internet via port-forwarding or other public access.
 Solis Agile Manager does not have any authentication, which means that a malicious hacker could easily control your 
 Inverter.

If you want to be able to access the application remotely, please ensure you only do so via a VPN or a reverse proxy.

## Installation

SolisManager runs as a server-based app, and is designed to run 24/7, in the background, with minimal 
interaction from the user. If it's working well, you shouldn't have to do anything once it's set up.

### Running as a local app on Linux/Mac/Windows/Raspberry Pi

To run it, go to the [latest release on GitHub](https://github.com/Webreaper/SolisAgileManager/releases/latest), 
and download the appropriate package for the operating systtem you're going to use (Mac/Linux/Windows/RPi).
Extract the zip into a folder and then run the main executable. Note that the config file and logs etc 
will be written to a folder called `Config` in the current working directory. If you'd rather they were
written somewhere else, pass your chosen folder as the first command-line parameter. 

Once the server is running, navigate to the UI via your browser. It will be at `http://localhost:5169`.

### Running via Docker 

You will need to pull the `webreaper/solisagilemanager` container, and map the internal port `5169` to the port
you want the web-UI exposed on from your host. You will also need to map a volume `/appdata` to a writeable
local folder on your host, so that the config, and log files are written outside the container (otherwise
when you pull a new version or recreate the container, you'll have to set up from scratch again).

Here's a sample `docker-compose` entry:

```
   solismanager:
        container_name: solismanager
        image: webreaper/solisagilemanager:latest
        ports:
            - '5169:5169'
        restart: unless-stopped
        volumes:
            - /volume1/dockerdata/solismanager:/appdata
```

Currently the only pre-built docker image is for `linux-x64` but I hope to add `linux-arm64` soon too.

## Settings

The first time you load the UI, you'll be prompted to input basic information, such as your Solis API key and 
secret, your inverter serial number, and the Octopus Product details of the current tariff you're on. Note 
that for this to work, you'll need to have raised a ticket with Solis to get access to control the inverter
via the SolisCloud app. 

<img width="600" alt="SettingsScreenshot" src="https://github.com/user-attachments/assets/2346f008-10e4-408b-a0ac-97b1af6c0a1c" />

You'll also need to set some other config setting:

* Max Charge Rate in Amps - set to the level that your battery can charge/discharge at.
* Charge slots for full battery - which tells the app how many slots of charging will be needed to go from
  empty to full. This will depend on your battery size and charging rate.
* Low Battery Threshold - the percentage at which you'd like to eagerly charge if prices are a bit lower
  than average
* The `Always charge below` rate. For example, if you set this to 10p/kWh, then _any_ slot lower than that
  price will always be set to charge, regardless of anything else.
* Simulate-only - if checked, the app will run and simulate what it _would have done_ without actually making
  any changes to the behaviour.

### Finding the right Tariff

Solis Manager defaults to a standard Agile tariff, but you probably want to set it to your _exact_ tariff to 
ensure it's accurate. To do this: 

* [Click here](https://api.octopus.energy/v1/products/) to get the list of Octopus Agile Product codes (e.g.,
  `AGILE-24-10-01`).
* Then once you've found your tariff, click on the URL in the `links` section to find your region-specific
  product code. For example: [https://api.octopus.energy/v1/products/AGILE-24-10-01/](https://api.octopus.energy/v1/products/AGILE-24-10-01/)
* From that page, copy the product code (e.g., `E-1R-AGILE-24-10-01-A`) into the Solis Manager settings.

Note that in the most common cases, selecting `AGILE-24-10-01` and `E-1R-AGILE-24-10-01-A`, and just altering 
the last `A` to the correct [Region Code](https://mysmartenergy.uk/Electricity-Region) will do exactly what you
need. Solis Manager doesn't do anything with the standing charge, so it doesn't matter if you're on an older
tariff. 

Once you've filled these in, the server will start running.

As it runs, the last 30 days' worth of charging decisions will be logged to `SolisManagerExecutionHistory.csv` 
so you can monitor the decisions it's taking to ensure they're as you require. There's also a History page that 
allows you to convienently check what it did, and why it did it:

<img width="1411" alt="HistoryView" src="https://github.com/user-attachments/assets/42078bb4-6f47-4adc-8f66-0f2f0c8e2eed" />
  
### Will the app work with non-Agile Tariffs?

I haven't tried, but it might!
  
### How does it work?

At first launch SolisManager will load the next set of Agile Tariff data, along with some information about
your inverter. It will then estimate the best charging strategy based on a number of rules, as set out below. 
Note that this strategy is based on my needs for battery-management, but should apply to many other people too.

### Who Will It Work For?

This application is based on a number of assumptions, the primary one of which is that the person running it 
is a high-consumption power user, probably with an Air Source Heat Pump or EV, and wants to optimise their 
battery charging to charge at the cheapest times. The goal of the app is to charge the battery at the cheapest
times possible, without too much unnecessary battery cycling. If you are a lower-useage household, you may find
that Rob Tweed's [Agility](https://github.com/robtweed/agility) app is better suited to your needs.

### The Algorithm/Strategy

* First, find the cheapest period for charging the battery. This is the set of contiguous
  slots, long enough when combined that they can charge the battery from empty to full, and
  that has the cheapest average price for that period. This will typically be around 1am in 
  the morning, but can shift around a bit.
* Then perform a similar calculation to find the most expensive (peak) period.
* Set the action for all cheapest slots to `Charge`
* Set the action for the most expensive slots to `Do Nothing` - i.e., don't charge.
* Now, we've calculated the cheapest and most expensive slots. From the remaining slots, calculate
  the average rate across them. We then use that average rate to determine if any other slots across
  the day are a bit cheaper. So look for anything that's 90% of the average or less, and mark it
  as `BelowAverage`. For those slots, if the battery is low, we'll take the opportunity to charge as 
  they're a bit cheaper-than-average, so set their action to `Charge If Low Battery`.
* If we have a set of cheapest slots, then the price will usually start to drop a few slots before
  it's actually cheapest; these will likely be slots that are BelowAverage pricing in the run-up to
  the cheapest period. However, we don't want to charge then, because otherwise by the time we
  get to the cheapest period, the battery will be full. So back up `n` slots and even if they're
  `BelowAverage`, set them to `Do Nothing`.
* If we have a set of priciest slots, we want to ensure the battery is charged before we reach them.
  So work backwards from the first expensive slot, applying a charge instruction on each slot before
  the priciest, until we've got enough slots to fully charge the battery.
  Note that we skip an extra one, because the slot before the priciest ones is almost always a bit more
  expensive too. Just in case it's REALLY expensive, allow ourselves a couple of steps back to look
  for slightly cheaper rates. Can't go back too far though otherwise the battery might not last through
  the peak period.
* If there are any slots below our "_Blimey it's cheap_" threshold, elect to charge them anyway. E.g.,
  we may elect to always charge if the price is negative, or if it's below our export rate.
* For any slots that are set to `Charge If Low Battery`, update them to 'charge' if the battery SOC is,
  indeed, low. Only do this for enough slots to fully charge the battery.

There are also _manual overrides_ which can be set vie the tools screen. For example:

* Charge the Battery - this will override the next `n` slots, regardless of cost, to completely
  charge the battery
* Discharge the Battery - this will override the next `n` slots to completely discharge the battery
* By clicking the `x` next to any charge/discharge slot, you can apply an override which will cancel
  the charge action and return that slot to `Do Nothing`.

### More on Simulation Mode

To test the app and ensure it functions correctly, simulation mode shows what would have happened,
without actually writing changes to the inverter. This allows you to see how the charging plan changes
with the strategy as different information comes in. 

Simulation mode is interactive; the first time the app loads it initialises the simulation using the
current inverter state, and the loaded Agile prices. As you advance through the simulation, it shows
what will happen (and the logs will show the commands that would have been sent to the inverter).

Once you run out of slots at the end of the simulation, click reset to start again.

### Technical Considerations

A couple of people have raised concerns about the number of writes a half-hourly process will make to the
SolisCloud API, and consequently the Inverter EEPROM. Excessive writes could result in a reduced longevity
of the EEPROM (which generally have a limit on the total number of writes they can manage).

To avoid this, the app applies Charging, Discharging and 'no charge' instructions in batches. So for
example, if the charging plan is as follows:

* 06:00-06:30 Do Nothing
* 06:30-07:00 Do Nothing
* 07:00-07:30 Charge
* 07:30-08:00 Charge
* 08:00-08:30 Charge
* 08:30-09:00 Charge
* 09:00-09:30 Do Nothing
* 09:30-10:00 Do Nothing

Then the actual calls are conflated to the following:

* 06:00 - Set inverter charge slot to 00:00-00:00 to turn off charging
* 07:00 - Set inverter charge slot to 07:00-09:00 to charge for 2 hours
* 09:00 - Set inverter charge slot to 00:00-00:00 to turn off charging

This optimisation means that the absolute minimum number of `control` API calls are made (from about 17,000 per
year down to around 2,000), and hence the minimum number of Inverter EEPROM writes are carried out.

### Coming Soon:

* A future feature enhancement is to allow the app to determine if Octopus prices fall below zero for a certain
  period, and if so to automatically dump the battery charge to the grid, and then recharge the battery.
* The app will read in Solcast forecast data if you provide an API key and Site ID. Currently this information
  is only used for display purposes, but will eventually be used to optimise the algorithm (e.g., by skipping
  overnight charging if the forecast is for a decent PV yield.

### Other Things I've Written

If you like this app, please check out some of my other applications, including a Photo Management system 
    called <strong><a href="https://github.com/webreaper/damselfly" target="_blank">Damselfly</a></strong>

  Solis Agile Manager is free, open-source software. But if you find it useful, and fancy buying me a coffee or a 
  slice of pizza, that would be appreciated! You can do this via my Damselfly BuyMeACoffee link.

  <div>
      <a href="https://www.buymeacoffee.com/damselfly" target="_blank">
          <img src="https://cdn.buymeacoffee.com/buttons/arial-yellow.png" alt="Buy Me A Coffee" height="41" width="174">
      </a>
  </div>

### Thanks/Credits

* Thanks to [Steve Gal](https://github.com/stevegal/solis_control) for his Solis-Control script that gave me the 
  info needed to build the API wrapper to set the charging slots on the inverter.
* Thanks also to [Jon Glass](https://github.com/jmg48/solis-cloud) for his sample .Net wrapper for the Solis API,
  without which I'd have spent an inordinate amount of time figuring out the complicated Solis Authentication
  process.
* Thanks must go to [Rob Tweed](https://github.com/robtweed) whose Agility project made me think about building
  this application.

### Technical Details

For those who are interested, the application is built using Blazor WebAssembly, with an ASP.Net back-end, with
[MudBlazor](https://github.com/mudblazor/mudblazor) used for the UI controls. The app is written entirely in C#, 
using .Net 9. It was written over the course of about 5 days. 
