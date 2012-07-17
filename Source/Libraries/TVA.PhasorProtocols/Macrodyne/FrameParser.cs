//******************************************************************************************************
//  FrameParser.cs - Gbtc
//
//  Copyright © 2012, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the Eclipse Public License -v 1.0 (the "License"); you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://www.opensource.org/licenses/eclipse-1.0.php
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  04/30/2009 - J. Ritchie Carroll
//       Generated original version of source code.
//  09/15/2009 - Stephen C. Wills
//       Added new header and license agreement.
//
//******************************************************************************************************

using System;
using System.IO;
using System.Text;
using System.Threading;
using TVA.IO;
using TVA.Parsing;

namespace TVA.PhasorProtocols.Macrodyne
{
    /// <summary>
    /// Represents a frame parser for a Macrodyne binary data stream that returns parsed data via events.
    /// </summary>
    /// <remarks>
    /// Frame parser is implemented as a write-only stream - this way data can come from any source.
    /// </remarks>
    public class FrameParser : FrameParserBase<FrameType>
    {
        #region [ Members ]

        // Events

        /// <summary>
        /// Occurs when a Macrodyne <see cref="HeaderFrame"/> that contains the UnitID (i.e., station name) has been received.
        /// </summary>
        /// <remarks>
        /// <see cref="EventArgs{T}.Argument"/> is the <see cref="HeaderFrame"/> that was received.
        /// </remarks>
        public new event EventHandler<EventArgs<HeaderFrame>> ReceivedHeaderFrame;

        /// <summary>
        /// Occurs when a Macrodyne <see cref="ConfigurationFrame"/> has been received.
        /// </summary>
        /// <remarks>
        /// <see cref="EventArgs{T}.Argument"/> is the <see cref="ConfigurationFrame"/> that was received.
        /// </remarks>
        public new event EventHandler<EventArgs<ConfigurationFrame>> ReceivedConfigurationFrame;

        /// <summary>
        /// Occurs when a Macrodyne <see cref="DataFrame"/> has been received.
        /// </summary>
        /// <remarks>
        /// <see cref="EventArgs{T}.Argument"/> is the <see cref="DataFrame"/> that was received.
        /// </remarks>
        public new event EventHandler<EventArgs<DataFrame>> ReceivedDataFrame;

        // Fields
        private HeaderFrame m_headerFrame;
        private ConfigurationFrame m_configurationFrame;
        private ProtocolVersion m_protocolVersion;
        private string m_configurationFileName;
        private string m_deviceLabel;
        private bool m_refreshConfigurationFileOnChange;
        private FileSystemWatcher m_configurationFileWatcher;
        private object m_syncLock;
        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="FrameParser"/>.
        /// </summary>
        public FrameParser()
        {
            m_protocolVersion = ProtocolVersion.M;
            m_syncLock = new object();
        }

        /// <summary>
        /// Creates a new <see cref="FrameParser"/> from specified parameters.
        /// </summary>
        /// <param name="protocolVersion">The protocol version that the parser should use.</param>
        /// <param name="configurationFileName">The optional external Macrodyne configuration in BPA PDCstream INI file based format.</param>
        /// <param name="deviceLabel">The INI section device label to use.</param>
        public FrameParser(ProtocolVersion protocolVersion, string configurationFileName, string deviceLabel)
            : this()
        {
            m_protocolVersion = protocolVersion;
            m_deviceLabel = deviceLabel;
            ConfigurationFileName = configurationFileName;
        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets or sets current <see cref="IConfigurationFrame"/> used for parsing <see cref="IDataFrame"/>'s encountered in the data stream from a device.
        /// </summary>
        /// <remarks>
        /// If a <see cref="IConfigurationFrame"/> has been parsed, this will return a reference to the parsed frame.  Consumer can manually assign a
        /// <see cref="IConfigurationFrame"/> to start parsing data if one has not been encountered in the stream.
        /// </remarks>
        public override IConfigurationFrame ConfigurationFrame
        {
            get
            {
                return m_configurationFrame;
            }
            set
            {
                m_configurationFrame = CastToDerivedConfigurationFrame(value, m_configurationFileName, m_deviceLabel);
            }
        }

        /// <summary>
        /// Gets flag that determines if Macrodyne protocol parsing implementation uses synchronization bytes.
        /// </summary>
        public override bool ProtocolUsesSyncBytes
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets or sets external Macrodyne based configuration file.
        /// </summary>
        public string ConfigurationFileName
        {
            get
            {
                if (m_configurationFrame == null)
                    return m_configurationFileName;
                else
                    return m_configurationFrame.ConfigurationFileName;
            }
            set
            {
                m_configurationFileName = value;
                ResetFileWatcher();

                if ((object)m_configurationFrame == null && !string.IsNullOrEmpty(m_configurationFileName) && File.Exists(m_configurationFileName))
                {
                    m_configurationFrame = new ConfigurationFrame(OnlineDataFormatFlags.TimestampEnabled, "1690", m_configurationFileName, m_deviceLabel);
                    m_configurationFrame.CommonHeader = new CommonFrameHeader(m_protocolVersion, FrameType.ConfigurationFrame);
                }
            }
        }

        /// <summary>
        /// Gets or sets device section label, as defined in associated INI file.
        /// </summary>
        public string DeviceLabel
        {
            get
            {
                return m_deviceLabel;
            }
            set
            {
                m_deviceLabel = value;
            }
        }

        /// <summary>
        /// Gets or sets flag that determines if configuration file is automatically reloaded when it has changed on disk.
        /// </summary>
        public bool RefreshConfigurationFileOnChange
        {
            get
            {
                return m_refreshConfigurationFileOnChange;
            }
            set
            {
                m_refreshConfigurationFileOnChange = value;
                ResetFileWatcher();
            }
        }

        /// <summary>
        /// Gets current descriptive status of the <see cref="FrameParser"/>.
        /// </summary>
        public override string Status
        {
            get
            {
                StringBuilder status = new StringBuilder();

                status.AppendFormat("Macrodyne protocol version: {0}", m_protocolVersion);
                status.AppendLine();
                status.AppendFormat("    INI configuration file: {0}", FilePath.TrimFileName(m_configurationFileName.ToNonNullNorEmptyString("undefined"), 51));
                status.AppendLine();
                status.AppendFormat("  Device INI section label: {0}", m_deviceLabel);
                status.AppendLine();
                status.AppendFormat(" Auto-reload configuration: {0}", m_refreshConfigurationFileOnChange);
                status.AppendLine();
                status.Append(base.Status);

                return status.ToString();
            }
        }

        /// <summary>
        /// Gets or sets any connection specific <see cref="IConnectionParameters"/> that may be needed for parsing.
        /// </summary>
        public override IConnectionParameters ConnectionParameters
        {
            get
            {
                return base.ConnectionParameters;
            }
            set
            {
                Macrodyne.ConnectionParameters parameters = value as Macrodyne.ConnectionParameters;

                if (parameters != null)
                {
                    base.ConnectionParameters = parameters;

                    // Assign new incoming connection parameter values
                    m_protocolVersion = parameters.ProtocolVersion;
                    m_deviceLabel = parameters.DeviceLabel;
                    ConfigurationFileName = parameters.ConfigurationFileName;
                    m_refreshConfigurationFileOnChange = parameters.RefreshConfigurationFileOnChange;
                    ResetFileWatcher();
                }
            }
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="FrameParser"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    if (disposing)
                    {
                        if (m_configurationFileWatcher != null)
                        {
                            m_configurationFileWatcher.Changed -= m_configurationFileWatcher_Changed;
                            m_configurationFileWatcher.Dispose();
                        }
                        m_configurationFileWatcher = null;
                    }
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        /// <summary>
        /// Start the data parser.
        /// </summary>
        public override void Start()
        {
            // We narrow down parsing types to just those needed...
            base.Start(new Type[] { typeof(DataFrame), typeof(HeaderFrame), typeof(ConfigurationFrame) });

            // Make sure we mark stream an initialized even though base class doesn't think we use sync-bytes
            StreamInitialized = false;

            // Publish configuration frame
            ThreadPool.QueueUserWorkItem(PublishConfigurationFrame);
        }

        /// <summary>
        /// Parses a common header instance that implements <see cref="ICommonHeader{TTypeIdentifier}"/> for the output type represented
        /// in the binary image.
        /// </summary>
        /// <param name="buffer">Buffer containing data to parse.</param>
        /// <param name="offset">Offset index into buffer that represents where to start parsing.</param>
        /// <param name="length">Maximum length of valid data from offset.</param>
        /// <returns>The <see cref="ICommonHeader{TTypeIdentifier}"/> which includes a type ID for the <see cref="Type"/> to be parsed.</returns>
        /// <remarks>
        /// <para>
        /// Derived classes need to provide a common header instance (i.e., class that implements <see cref="ICommonHeader{TTypeIdentifier}"/>)
        /// for the output types; this will primarily include an ID of the <see cref="Type"/> that the data image represents.  This parsing is
        /// only for common header information, actual parsing will be handled by output type via its <see cref="ISupportBinaryImage.ParseBinaryImage"/>
        /// method. This header image should also be used to add needed complex state information about the output type being parsed if needed.
        /// </para>
        /// <para>
        /// If there is not enough buffer available to parse common header (as determined by <paramref name="length"/>), return null.  Also, if
        /// the protocol allows frame length to be determined at the time common header is being parsed and there is not enough buffer to parse
        /// the entire frame, it will be optimal to prevent further parsing by returning null.
        /// </para>
        /// </remarks>
        protected override ICommonHeader<FrameType> ParseCommonHeader(byte[] buffer, int offset, int length)
        {
            // See if there is enough data in the buffer to parse the common frame header.
            if (length >= CommonFrameHeader.FixedLength)
            {
                // Parse common frame header
                CommonFrameHeader parsedFrameHeader = new CommonFrameHeader(buffer, offset, m_protocolVersion, m_configurationFrame);

                // As an optimization, we also make sure entire frame buffer image is available to be parsed - by doing this
                // we eliminate the need to validate length on all subsequent data elements that comprise the frame
                if (length >= parsedFrameHeader.FrameLength)
                {
                    // Expose the frame buffer image in case client needs this data for any reason
                    OnReceivedFrameBufferImage(parsedFrameHeader.FrameType, buffer, offset, length);

                    // Handle special parsing states
                    switch (parsedFrameHeader.TypeID)
                    {
                        case FrameType.DataFrame:
                            // Assign data frame parsing state
                            parsedFrameHeader.State = new DataFrameParsingState(parsedFrameHeader.FrameLength, m_configurationFrame, DataCell.CreateNewCell);
                            break;
                        case FrameType.HeaderFrame:
                            // Assign header frame parsing state
                            parsedFrameHeader.State = new HeaderFrameParsingState(parsedFrameHeader.FrameLength, parsedFrameHeader.DataLength);
                            break;
                        case FrameType.ConfigurationFrame:
                            // Assign configuration frame parsing state
                            parsedFrameHeader.State = new ConfigurationFrameParsingState(parsedFrameHeader.FrameLength, m_headerFrame, ConfigurationCell.CreateNewCell);
                            break;
                    }

                    return parsedFrameHeader;
                }
            }

            return null;
        }

        /// <summary>
        /// Writes a sequence of bytes onto the stream for parsing.
        /// </summary>
        /// <param name="buffer">An array of bytes. This method copies count bytes from buffer to the current stream.</param>
        /// <param name="offset">The zero-based byte offset in buffer at which to begin copying bytes to the current stream.</param>
        /// <param name="count">The number of bytes to be written to the current stream.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            // Since the Macrodyne implementation supports both 0xAA and 0xBB as sync-bytes, we must manually check for both during stream initialization,
            // base class handles this only then there is a consistently defined set of sync-bytes, not variable.

            if (Enabled)
            {
                // See if there are any 0xAA 0xAA sequences - these must be removed
                int syncBytePosition = buffer.IndexOfSequence(new byte[] { 0xAA, 0xAA }, offset, count);

                while (syncBytePosition > -1)
                {
                    MemoryStream newBuffer = new MemoryStream();

                    // Write buffer before repeated byte
                    newBuffer.Write(buffer, offset, syncBytePosition - offset + 1);

                    int nextByte = syncBytePosition + 2;

                    // Write buffer after repeated byte, if any
                    if (nextByte < offset + count)
                        newBuffer.Write(buffer, nextByte, offset + count - nextByte);

                    buffer = newBuffer.ToArray();
                    offset = 0;
                    count = buffer.Length;

                    // Find next 0xAA 0xAA sequence
                    syncBytePosition = buffer.IndexOfSequence(new byte[] { 0xAA, 0xAA }, offset, count);
                }

                if (StreamInitialized)
                {
                    base.Write(buffer, offset, count);
                }
                else
                {
                    // Initial stream may be anywhere in the middle of a frame, so we attempt to locate sync-bytes to "line-up" data stream,
                    // First we look for data frame sync-byte:
                    syncBytePosition = buffer.IndexOfSequence(new byte[] { 0xAA }, offset, count);

                    if (syncBytePosition > -1)
                    {
                        StreamInitialized = true;
                        base.Write(buffer, syncBytePosition, count - (syncBytePosition - offset));
                    }
                    else
                    {
                        // Second we look for command frame response sync-byte:
                        syncBytePosition = buffer.IndexOfSequence(new byte[] { 0xBB }, offset, count);

                        if (syncBytePosition > -1)
                        {
                            StreamInitialized = true;
                            base.Write(buffer, syncBytePosition, count - (syncBytePosition - offset));
                        }
                    }
                }
            }
        }

        // Publish the current configuration frame
        private void PublishConfigurationFrame(object state)
        {
            try
            {
                if ((object)m_configurationFrame != null)
                    OnReceivedConfigurationFrame(m_configurationFrame);
            }
            catch (ThreadAbortException)
            {
                throw;
            }
            catch (Exception ex)
            {
                OnParsingException(ex);
            }
        }

        // Handler for file watcher - we notify consumer when changes have occured to configuration file
        private void m_configurationFileWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            // We synchronize change actions - don't want more than one refresh happening at a time...
            lock (m_syncLock)
            {
                // Notify consumer of change in configuration
                OnConfigurationChanged();

                // Reload configuration...
                if ((object)m_configurationFrame != null)
                    m_configurationFrame.Refresh();
            }
        }

        // Reset file watcher
        private void ResetFileWatcher()
        {
            if (m_configurationFileWatcher != null)
            {
                m_configurationFileWatcher.Changed -= m_configurationFileWatcher_Changed;
                m_configurationFileWatcher.Dispose();
            }
            m_configurationFileWatcher = null;

            string configurationFile = ConfigurationFileName;

            if (m_refreshConfigurationFileOnChange && !string.IsNullOrEmpty(configurationFile) && File.Exists(configurationFile))
            {
                try
                {
                    // Create a new file watcher for configuration file - we'll automatically refresh configuration file
                    // when this file gets updated...
                    m_configurationFileWatcher = new FileSystemWatcher(FilePath.GetDirectoryName(configurationFile), FilePath.GetFileName(configurationFile));
                    m_configurationFileWatcher.Changed += m_configurationFileWatcher_Changed;
                    m_configurationFileWatcher.EnableRaisingEvents = true;
                    m_configurationFileWatcher.IncludeSubdirectories = false;
                    m_configurationFileWatcher.NotifyFilter = NotifyFilters.LastWrite;
                }
                catch (Exception ex)
                {
                    OnParsingException(ex);
                }
            }
        }

        /// <summary>
        /// Raises the <see cref="FrameParserBase{TypeIndentifier}.ReceivedHeaderFrame"/> event.
        /// </summary>
        /// <param name="frame"><see cref="IHeaderFrame"/> to send to <see cref="FrameParserBase{TypeIndentifier}.ReceivedHeaderFrame"/> event.</param>
        protected override void OnReceivedHeaderFrame(IHeaderFrame frame)
        {
            // We override this method so we can cache header frame when it's received
            base.OnReceivedHeaderFrame(frame);

            //// Cache new header frame for parsing subsequent configuration frame...
            HeaderFrame headerFrame = frame as HeaderFrame;

            if (headerFrame != null)
                m_headerFrame = headerFrame;
        }

        /// <summary>
        /// Casts the parsed <see cref="IChannelFrame"/> to its specific implementation (i.e., <see cref="DataFrame"/> or <see cref="ConfigurationFrame"/>).
        /// </summary>
        /// <param name="frame"><see cref="IChannelFrame"/> that was parsed by <see cref="FrameImageParserBase{TTypeIdentifier,TOutputType}"/> that implements protocol specific common frame header interface.</param>
        protected override void OnReceivedChannelFrame(IChannelFrame frame)
        {
            // Raise abstract channel frame events as a priority (i.e., IDataFrame, IConfigurationFrame, etc.)
            base.OnReceivedChannelFrame(frame);

            // Raise Macrodyne specific channel frame events, if any have been subscribed
            if (frame != null && (ReceivedDataFrame != null || ReceivedConfigurationFrame != null || ReceivedHeaderFrame != null))
            {
                DataFrame dataFrame = frame as DataFrame;

                if (dataFrame != null)
                {
                    if (ReceivedDataFrame != null)
                        ReceivedDataFrame(this, new EventArgs<DataFrame>(dataFrame));
                }
                else
                {
                    HeaderFrame headerFrame = frame as HeaderFrame;

                    if (headerFrame != null)
                    {
                        if (ReceivedHeaderFrame != null)
                            ReceivedHeaderFrame(this, new EventArgs<HeaderFrame>(headerFrame));
                    }
                    else
                    {
                        ConfigurationFrame configFrame = frame as ConfigurationFrame;

                        if (configFrame != null)
                        {
                            if (ReceivedConfigurationFrame != null)
                                ReceivedConfigurationFrame(this, new EventArgs<ConfigurationFrame>(configFrame));
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Raises the <see cref="BinaryImageParserBase.ParsingException"/> event.
        /// </summary>
        /// <param name="ex">The <see cref="Exception"/> that was encountered during parsing.</param>
        protected override void OnParsingException(Exception ex)
        {
            base.OnParsingException(ex);

            // At the first sign of an error, we need to reset stream initialization flag - could just be looping a saved file source or we missed some data,
            // just need to resync to next 0xAA byte...
            StreamInitialized = false;
        }

        #endregion

        #region [ Static ]

        // Attempts to cast given frame into a Macrodyne configuration frame - theoretically this will
        // allow the same configuration frame to be used for any protocol implementation
        internal static ConfigurationFrame CastToDerivedConfigurationFrame(IConfigurationFrame sourceFrame, string configurationFileName, string deviceLabel)
        {
            // See if frame is already a Macrodyne frame (if so, we don't need to do any work)
            ConfigurationFrame derivedFrame = sourceFrame as ConfigurationFrame;

            if (derivedFrame == null)
            {
                // Create a new Macrodyne configuration frame converted from equivalent configuration information; Macrodyne only supports one device
                if (sourceFrame.Cells.Count > 0)
                {
                    IConfigurationCell sourceCell = sourceFrame.Cells[0];
                    string stationName = sourceCell.StationName;

                    if (string.IsNullOrEmpty(stationName))
                        stationName = "Unit " + sourceCell.IDCode.ToString();

                    stationName = stationName.TruncateLeft(8);
                    derivedFrame = new ConfigurationFrame(Common.GetFormatFlagsFromPhasorCount(sourceFrame.Cells[0].PhasorDefinitions.Count), stationName, configurationFileName, deviceLabel);
                    derivedFrame.IDCode = sourceFrame.IDCode;

                    // Create new derived configuration cell
                    ConfigurationCell derivedCell = new ConfigurationCell(derivedFrame);
                    IFrequencyDefinition sourceFrequency;

                    // Create equivalent derived phasor definitions
                    foreach (IPhasorDefinition sourcePhasor in sourceCell.PhasorDefinitions)
                    {
                        derivedCell.PhasorDefinitions.Add(new PhasorDefinition(derivedCell, sourcePhasor.Label, sourcePhasor.PhasorType, null));
                    }

                    // Create equivalent dervied frequency definition
                    sourceFrequency = sourceCell.FrequencyDefinition;

                    if (sourceFrequency != null)
                    {
                        derivedCell.FrequencyDefinition = new FrequencyDefinition(derivedCell)
                        {
                            Label = sourceFrequency.Label
                        };
                    }

                    // Create equivalent dervied digital definitions
                    foreach (IDigitalDefinition sourceDigital in sourceCell.DigitalDefinitions)
                    {
                        derivedCell.DigitalDefinitions.Add(new DigitalDefinition(derivedCell, sourceDigital.Label));
                    }

                    // Add cell to frame
                    derivedFrame.Cells.Add(derivedCell);
                }
            }

            return derivedFrame;
        }

        #endregion
    }
}