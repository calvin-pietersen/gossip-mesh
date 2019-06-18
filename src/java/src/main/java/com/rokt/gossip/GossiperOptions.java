package com.gossipmesh.core;

public class GossiperOptions {

    private int protocolPeriodMs = 1000;
    private int pingTimeoutMs = 200;
    private int indirectPingTimeoutMs = 400;
    private int deathTimeoutMs = 60000;
    private int fanoutFactor = 3;
    private int numberOfIndirectEndPoints = 3;

    public int getProtocolPeriodMs() { return protocolPeriodMs; }
    public int getPingTimeoutMs() { return pingTimeoutMs; }
    public int getIndirectPingTimeoutMs() { return indirectPingTimeoutMs; }
    public int getDeathTimeoutMs() { return deathTimeoutMs; }
    public int getFanoutFactor() { return fanoutFactor; }
    public int getNumberOfIndirectEndPoints() { return numberOfIndirectEndPoints; }

    public void setProtocolPeriodMs(int v) { protocolPeriodMs = v; }
    public void setPingTimeoutMs(int v) { pingTimeoutMs = v; }
    public void setIndirectPingTimeoutMs(int v) { indirectPingTimeoutMs = v; }
    public void setDeathTimeoutMs(int v) { deathTimeoutMs = v; }
    public void setFanoutFactor(int v) { fanoutFactor = v; }
    public void setNumberOfIndirectEndPoints(int v) { numberOfIndirectEndPoints = v; }
}
