-- phpMyAdmin SQL Dump
-- version 3.2.4
-- http://www.phpmyadmin.net
--
-- Host: localhost
-- Generation Time: Oct 30, 2012 at 12:03 PM
-- Server version: 5.1.41
-- PHP Version: 5.3.1

SET SQL_MODE="NO_AUTO_VALUE_ON_ZERO";


/*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
/*!40101 SET @OLD_CHARACTER_SET_RESULTS=@@CHARACTER_SET_RESULTS */;
/*!40101 SET @OLD_COLLATION_CONNECTION=@@COLLATION_CONNECTION */;
/*!40101 SET NAMES utf8 */;

--
-- Database: `gis`
--

-- --------------------------------------------------------

--
-- Table structure for table `bookmark`
--

CREATE TABLE IF NOT EXISTS `bookmark` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `entry` int(11) NOT NULL,
  `time_hours` double(10,2) NOT NULL,
  `time_minutes` double(10,2) NOT NULL,
  `time_seconds` double(10,2) NOT NULL,
  `title` varchar(200) NOT NULL,
  `notes` varchar(1000) NOT NULL,
  `attachment` varchar(100) DEFAULT NULL,
  PRIMARY KEY (`id`),
  KEY `entry` (`entry`)
) ENGINE=InnoDB  DEFAULT CHARSET=latin1 AUTO_INCREMENT=45 ;

--
-- Dumping data for table `bookmark`
--

INSERT INTO `bookmark` (`id`, `entry`, `time_hours`, `time_minutes`, `time_seconds`, `title`, `notes`, `attachment`) VALUES
(33, 9, 0.00, 0.00, 1.51, 'bookmark A', 'test', ''),
(34, 9, 0.00, 0.00, 10.93, 'bookmark B', 'BOOKMARK 2', ''),
(36, 9, 0.00, 0.00, 15.24, 'ddd bookmark', 'test3', ''),
(44, 9, 0.00, 0.00, 23.36, 'attachment test', 'attachment test', 'sample attachment28.txt');

-- --------------------------------------------------------

--
-- Table structure for table `entry`
--

CREATE TABLE IF NOT EXISTS `entry` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `video_1` varchar(200) NOT NULL,
  `video_2` varchar(200) NOT NULL,
  `video_3` varchar(200) NOT NULL,
  `video_4` varchar(200) NOT NULL,
  `video_5` varchar(200) NOT NULL,
  `video_6` varchar(200) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB  DEFAULT CHARSET=latin1 AUTO_INCREMENT=10 ;

--
-- Dumping data for table `entry`
--

INSERT INTO `entry` (`id`, `video_1`, `video_2`, `video_3`, `video_4`, `video_5`, `video_6`) VALUES
(9, 'video_1.mp4', 'video_2.mp4', 'video_3.mp4', 'video_4.mp4', 'video_5.mp4', 'video_6.mp4');

-- --------------------------------------------------------

--
-- Table structure for table `map`
--

CREATE TABLE IF NOT EXISTS `map` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `entry` int(11) NOT NULL,
  `time_hours` int(11) NOT NULL,
  `time_minutes` int(11) NOT NULL,
  `time_seconds` int(11) NOT NULL,
  `latitude` double(10,6) NOT NULL,
  `longitude` double(10,6) NOT NULL,
  PRIMARY KEY (`id`),
  KEY `entry` (`entry`)
) ENGINE=InnoDB  DEFAULT CHARSET=latin1 AUTO_INCREMENT=6 ;

--
-- Dumping data for table `map`
--

INSERT INTO `map` (`id`, `entry`, `time_hours`, `time_minutes`, `time_seconds`, `latitude`, `longitude`) VALUES
(3, 9, 0, 0, 1, 6.927108, 79.861336),
(4, 9, 0, 0, 3, -22.903539, -43.209587),
(5, 9, 0, 0, 5, -37.827141, 144.970093);

--
-- Constraints for dumped tables
--

--
-- Constraints for table `bookmark`
--
ALTER TABLE `bookmark`
  ADD CONSTRAINT `bookmark_ibfk_1` FOREIGN KEY (`entry`) REFERENCES `entry` (`id`) ON DELETE CASCADE;

--
-- Constraints for table `map`
--
ALTER TABLE `map`
  ADD CONSTRAINT `map_ibfk_1` FOREIGN KEY (`entry`) REFERENCES `entry` (`id`) ON DELETE CASCADE;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
